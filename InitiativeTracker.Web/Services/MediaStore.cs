using InitiativeTracker.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace InitiativeTracker.Web.Services;

/// <summary>One writer/decoder per hub process. Originals never enter persistent storage.</summary>
public sealed class MediaStore
{
    public const long MaxStorageBytes = 128L * 1024 * 1024;
    public const long MaxProfileBytes = 20L * 1024 * 1024;
    public const int MaxAssets = 512;
    public const int MaxProfileAssets = 64;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly IDbContextFactory<InitiativeTrackerDbContext> factory;
    private readonly ImageNormalizer normalizer;
    private readonly ILogger<MediaStore> logger;
    public string DirectoryPath { get; }
    public bool AllowsPersistentWrites { get; }

    public MediaStore(IDbContextFactory<InitiativeTrackerDbContext> factory, ImageNormalizer normalizer,
        ILogger<MediaStore> logger, string dataDirectory, bool allowWrites = true)
    {
        this.factory = factory;
        this.normalizer = normalizer;
        this.logger = logger;
        DirectoryPath = Path.Combine(dataDirectory, "Media");
        AllowsPersistentWrites = allowWrites;
        if (new DirectoryInfo(DirectoryPath).LinkTarget is not null) throw new IOException("Media directory cannot be a symbolic link.");
        if (!allowWrites) return;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(DirectoryPath);
        else Directory.CreateDirectory(DirectoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public async Task ReplaceAsync(string scope, Guid subjectId, string kind, IFormFile upload,
        Func<MediaSubject?> authorize, CancellationToken cancellationToken = default)
    {
        if (!MediaPaths.IsAllowed(scope, kind) || authorize() is null) throw new UnauthorizedAccessException();
        if (!AllowsPersistentWrites) throw new MediaValidationException("Configure persistent Tracker:DataDirectory outside the application directory before changing images.");
        if (!await gate.WaitAsync(0, cancellationToken)) throw new MediaBusyException();
        try
        {
            if (upload.Length <= 0 || upload.Length > ImageNormalizer.MaxUploadBytes)
                throw new MediaValidationException("Choose an image of at most 5 MiB.");
            byte[] input;
            using (var memory = new MemoryStream())
            await using (var stream = upload.OpenReadStream())
            {
                var buffer = new byte[64 * 1024];
                int count;
                while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    if (memory.Length + count > ImageNormalizer.MaxUploadBytes)
                        throw new MediaValidationException("Choose an image of at most 5 MiB.");
                    memory.Write(buffer, 0, count);
                }
                input = memory.ToArray();
            }
            if (authorize() is null) throw new UnauthorizedAccessException();
            var image = normalizer.Normalize(input, kind);
            cancellationToken.ThrowIfCancellationRequested();
            var subject = authorize() ?? throw new UnauthorizedAccessException();
            using var db = factory.CreateDbContext();
            Prune(db);
            var row = db.PresentationAssets.Find(scope, subjectId, kind);
            var previousId = row?.BlobId;
            var previousPath = previousId.HasValue ? BlobPath(previousId.Value) : null;
            var previousBytes = previousPath is not null && File.Exists(previousPath)
                && !File.GetAttributes(previousPath).HasFlag(FileAttributes.ReparsePoint) ? new FileInfo(previousPath).Length : 0;
            var rows = db.PresentationAssets.AsNoTracking().ToList();
            var owned = rows.Where(x => x.OwnerProfileId == subject.OwnerProfileId).ToList();
            if ((row is null && (rows.Count >= MaxAssets || (subject.OwnerProfileId.HasValue && owned.Count >= MaxProfileAssets)))
                || (subject.OwnerProfileId.HasValue && owned.Sum(x => x.Length) - (row?.Length ?? 0) + image.Bytes.Length > MaxProfileBytes)
                || StoredBytes() - previousBytes + image.Bytes.Length > MaxStorageBytes)
                throw new MediaValidationException("Image storage limit reached. Remove an unused portrait or banner first.");
            var blobId = Guid.NewGuid();
            var temporary = Path.Combine(DirectoryPath, blobId.ToString("N") + ".tmp");
            var destination = BlobPath(blobId);
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.WriteThrough };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (var output = new FileStream(temporary, options))
                {
                    output.Write(image.Bytes);
                    output.Flush(flushToDisk: true);
                }
                File.Move(temporary, destination);
                if (row is null)
                {
                    row = new PresentationAsset { Scope = scope, SubjectId = subjectId, Kind = kind };
                    db.PresentationAssets.Add(row);
                }
                row.BlobId = blobId;
                row.OwnerProfileId = subject.OwnerProfileId;
                row.Width = image.Width;
                row.Height = image.Height;
                row.Length = image.Bytes.Length;
                row.UpdatedAtUtc = DateTime.UtcNow;
                db.SaveChanges();
            }
            catch
            {
                TryDelete(temporary);
                TryDelete(destination);
                throw;
            }
            if (previousId.HasValue) TryDelete(BlobPath(previousId.Value));
        }
        finally { gate.Release(); }
    }

    public async Task RemoveAsync(string scope, Guid id, string kind, Func<MediaSubject?> authorize, CancellationToken cancellationToken = default)
    {
        if (!MediaPaths.IsAllowed(scope, kind) || authorize() is null) throw new UnauthorizedAccessException();
        if (!AllowsPersistentWrites) throw new MediaValidationException("Configure persistent Tracker:DataDirectory outside the application directory before changing images.");
        if (!await gate.WaitAsync(0, cancellationToken)) throw new MediaBusyException();
        try
        {
            if (authorize() is null) throw new UnauthorizedAccessException();
            using var db = factory.CreateDbContext();
            var row = db.PresentationAssets.Find(scope, id, kind);
            if (row is null) return;
            db.Remove(row);
            db.SaveChanges();
            TryDelete(BlobPath(row.BlobId));
            Prune(db);
        }
        finally { gate.Release(); }
    }

    public byte[]? Read(string scope, Guid id, string kind)
    {
        if (!MediaPaths.IsAllowed(scope, kind)) return null;
        using var db = factory.CreateDbContext();
        var row = db.PresentationAssets.AsNoTracking().SingleOrDefault(x => x.Scope == scope && x.SubjectId == id && x.Kind == kind);
        if (row is null) return null;
        var path = BlobPath(row.BlobId);
        try
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return null;
            using var stream = File.OpenRead(path);
            if (stream.Length != row.Length || stream.Length > ImageNormalizer.MaxOutputBytes) return null;
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public async Task PruneAsync(CancellationToken cancellationToken = default)
    {
        if (!AllowsPersistentWrites) return;
        if (!await gate.WaitAsync(0, cancellationToken)) return;
        try { using var db = factory.CreateDbContext(); Prune(db); }
        finally { gate.Release(); }
    }

    public static bool OutsideApplicationDirectory(string dataDirectory, string contentRoot)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(contentRoot), Path.GetFullPath(dataDirectory));
        return relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative);
    }

    private void Prune(InitiativeTrackerDbContext db)
    {
        var rows = db.PresentationAssets.ToList();
        var characterSubjects = rows.Where(x => x.Scope == "character").Select(x => x.SubjectId).ToList();
        var profileSubjects = rows.Where(x => x.Scope == "profile").Select(x => x.SubjectId).ToList();
        var characters = db.PlayerCharacters.Where(x => x.IsActive && characterSubjects.Contains(x.Id)).Select(x => x.Id).ToHashSet();
        var profiles = db.PlayerProfiles.Where(x => profileSubjects.Contains(x.Id)).Select(x => x.Id).ToHashSet();
        foreach (var row in rows.Where(x => (x.Scope == "character" && !characters.Contains(x.SubjectId))
                                           || (x.Scope == "profile" && !profiles.Contains(x.SubjectId)))) db.Remove(row);
        db.SaveChanges();
        var referenced = db.PresentationAssets.AsNoTracking().Select(x => x.BlobId).ToHashSet();
        foreach (var path in Directory.EnumerateFiles(DirectoryPath))
        {
            var extension = Path.GetExtension(path);
            if (extension is not (".png" or ".tmp") || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id)) continue;
            if (extension == ".tmp" || !referenced.Contains(id)) TryDelete(path);
        }
    }

    private long StoredBytes() => Directory.EnumerateFiles(DirectoryPath).Sum(path => new FileInfo(path).Length);
    private string BlobPath(Guid id) => Path.Combine(DirectoryPath, id.ToString("N") + ".png");
    private void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException exception) { logger.LogWarning(exception, "Image cleanup deferred until the next maintenance pass."); }
        catch (UnauthorizedAccessException exception) { logger.LogWarning(exception, "Image cleanup deferred until permissions are restored."); }
    }
}

public sealed class MediaMaintenanceService(MediaStore store, ILogger<MediaMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try { await store.PruneAsync(stoppingToken); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            { logger.LogWarning(exception, "Image maintenance deferred."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
