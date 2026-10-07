using InitiativeTracker.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace InitiativeTracker.Web.Services;

public sealed class SystemHealthService
{
    public const int HubProtocolVersion =
        1;

    public const int ReleaseManifestFormatVersion =
        1;

    private readonly IDbContextFactory<InitiativeTrackerDbContext>
        _dbContextFactory;

    private readonly AppVersionService
        _appVersion;

    private readonly HubIdentityService
        _hubIdentity;

    private readonly string
        _dataDirectory;


    public SystemHealthService(
        IDbContextFactory<InitiativeTrackerDbContext> dbContextFactory,
        AppVersionService appVersion,
        HubIdentityService hubIdentity,
        string dataDirectory)
    {
        _dbContextFactory =
            dbContextFactory;

        _appVersion =
            appVersion;

        _hubIdentity =
            hubIdentity;

        _dataDirectory =
            dataDirectory;
    }


    public async Task<SystemHealthSnapshot>
        CheckAsync(
            CancellationToken cancellationToken =
                default)
    {
        var checkedAtUtc =
            DateTimeOffset.UtcNow;

        var databaseConnected =
            false;

        var appliedMigrations =
            new List<string>();

        var pendingMigrations =
            new List<string>();

        string? databaseError =
            null;


        try
        {
            await using var db =
                await _dbContextFactory
                    .CreateDbContextAsync(
                        cancellationToken);

            databaseConnected =
                await db.Database
                    .CanConnectAsync(
                        cancellationToken);

            if (databaseConnected)
            {
                appliedMigrations =
                    (await db.Database
                        .GetAppliedMigrationsAsync(
                            cancellationToken))
                    .ToList();

                pendingMigrations =
                    (await db.Database
                        .GetPendingMigrationsAsync(
                            cancellationToken))
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            databaseError =
                ex.Message;
        }


        var dataDirectoryReady =
            Directory.Exists(
                _dataDirectory);

        var availableStorageBytes =
            TryGetAvailableStorageBytes(
                _dataDirectory);


        var healthy =
            databaseConnected &&
            pendingMigrations.Count == 0 &&
            dataDirectoryReady;


        return new SystemHealthSnapshot
        {
            Healthy =
                healthy,

            CheckedAtUtc =
                checkedAtUtc,

            Product =
                _appVersion.Product,

            Version =
                _appVersion.Version,

            HubId =
                _hubIdentity.HubId,

            HubHostName =
                _hubIdentity.HostName,

            HubProtocolVersion =
                HubProtocolVersion,

            ReleaseManifestFormatVersion =
                ReleaseManifestFormatVersion,

            DatabaseConnected =
                databaseConnected,

            LatestAppliedMigration =
                appliedMigrations
                    .LastOrDefault(),

            PendingMigrations =
                pendingMigrations,

            DatabaseError =
                databaseError,

            DataDirectoryReady =
                dataDirectoryReady,

            AvailableStorageBytes =
                availableStorageBytes,

            StartedAtUtc =
                _appVersion.StartedAtUtc,

            Uptime =
                _appVersion.Uptime,

            InformationalVersion =
                _appVersion.InformationalVersion,

            AssemblyVersion =
                _appVersion.AssemblyVersion,

            Runtime =
                _appVersion.Runtime,

            Architecture =
                _appVersion.Architecture,

            OperatingSystem =
                _appVersion.OperatingSystem
        };
    }


    private static long?
        TryGetAvailableStorageBytes(
            string directory)
    {
        try
        {
            var fullPath =
                Path.GetFullPath(
                    directory);

            var root =
                Path.GetPathRoot(
                    fullPath);

            if (string.IsNullOrWhiteSpace(
                    root))
            {
                return null;
            }

            return new DriveInfo(
                    root)
                .AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }
}


public sealed class SystemHealthSnapshot
{
    public bool Healthy { get; init; }

    public DateTimeOffset CheckedAtUtc { get; init; }

    public string Product { get; init; } =
        string.Empty;

    public string Version { get; init; } =
        string.Empty;

    public Guid HubId { get; init; }

    public string HubHostName { get; init; } =
        string.Empty;

    public int HubProtocolVersion { get; init; }

    public int ReleaseManifestFormatVersion { get; init; }

    public bool DatabaseConnected { get; init; }

    public string? LatestAppliedMigration { get; init; }

    public IReadOnlyList<string> PendingMigrations { get; init; } =
        [];

    public string? DatabaseError { get; init; }

    public bool DataDirectoryReady { get; init; }

    public long? AvailableStorageBytes { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public TimeSpan Uptime { get; init; }

    public string InformationalVersion { get; init; } =
        string.Empty;

    public string AssemblyVersion { get; init; } =
        string.Empty;

    public string Runtime { get; init; } =
        string.Empty;

    public string Architecture { get; init; } =
        string.Empty;

    public string OperatingSystem { get; init; } =
        string.Empty;
}
