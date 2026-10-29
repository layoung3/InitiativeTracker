using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace InitiativeTracker.Web.Updates;

public sealed class ReleasePackageVerifier
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

    public async Task<ReleaseVerificationResult> VerifyAsync(
        string manifestPath,
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        if (!File.Exists(manifestPath))
        {
            errors.Add("Release manifest file was not found.");
            return ReleaseVerificationResult.Failed(errors);
        }

        if (!File.Exists(packagePath))
        {
            errors.Add("Release package file was not found.");
            return ReleaseVerificationResult.Failed(errors);
        }

        ReleaseManifest? manifest;

        try
        {
            await using var manifestStream = File.OpenRead(manifestPath);

            manifest =
                await JsonSerializer.DeserializeAsync<ReleaseManifest>(
                    manifestStream,
                    JsonOptions,
                    cancellationToken);
        }
        catch (Exception ex)
            when (ex is JsonException
                  or IOException
                  or UnauthorizedAccessException)
        {
            errors.Add($"Release manifest could not be read: {ex.Message}");
            return ReleaseVerificationResult.Failed(errors);
        }

        errors.AddRange(
            ReleaseManifestValidator.Validate(manifest));

        if (manifest is null)
        {
            return ReleaseVerificationResult.Failed(errors);
        }

        var packageFileName =
            Path.GetFileName(packagePath);

        if (!string.Equals(
                packageFileName,
                manifest.Package.FileName,
                StringComparison.Ordinal))
        {
            errors.Add(
                "Package file name does not match the manifest.");
        }

        var packageInfo =
            new FileInfo(packagePath);

        if (packageInfo.Length !=
            manifest.Package.SizeBytes)
        {
            errors.Add(
                $"Package size does not match the manifest. Expected {manifest.Package.SizeBytes} bytes but found {packageInfo.Length} bytes.");
        }

        string actualSha256;

        try
        {
            actualSha256 =
                await CalculateSha256Async(
                    packagePath,
                    cancellationToken);
        }
        catch (Exception ex)
            when (ex is IOException
                  or UnauthorizedAccessException)
        {
            errors.Add(
                $"Package checksum could not be calculated: {ex.Message}");

            return ReleaseVerificationResult.Failed(
                errors,
                manifest);
        }

        if (!string.Equals(
                actualSha256,
                manifest.Package.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                "Package SHA-256 does not match the manifest.");
        }

        errors.AddRange(
            InspectArchive(packagePath));

        return errors.Count == 0
            ? ReleaseVerificationResult.Succeeded(
                manifest,
                actualSha256)
            : ReleaseVerificationResult.Failed(
                errors,
                manifest,
                actualSha256);
    }

    private static async Task<string> CalculateSha256Async(
        string packagePath,
        CancellationToken cancellationToken)
    {
        await using var stream =
            new FileStream(
                packagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 128,
                FileOptions.Asynchronous |
                FileOptions.SequentialScan);

        var hash =
            await SHA256.HashDataAsync(
                stream,
                cancellationToken);

        return Convert
            .ToHexString(hash)
            .ToLowerInvariant();
    }

    private static IReadOnlyList<string> InspectArchive(
        string packagePath)
    {
        var errors =
            new List<string>();

        var foundAnyEntry = false;
        var foundApplicationExecutable = false;

        try
        {
            using var fileStream =
                File.OpenRead(packagePath);

            using var gzipStream =
                new GZipStream(
                    fileStream,
                    CompressionMode.Decompress);

            using var tarReader =
                new TarReader(gzipStream);

            TarEntry? entry;

            while ((entry = tarReader.GetNextEntry()) is not null)
            {
                foundAnyEntry = true;

                var entryName =
                    NormalizeEntryName(entry.Name);

                if (string.IsNullOrWhiteSpace(entryName))
                {
                    continue;
                }

                if (IsUnsafeArchivePath(entryName))
                {
                    errors.Add(
                        $"Archive contains an unsafe path: '{entry.Name}'.");

                    continue;
                }

                var rootRelative =
                    entryName.StartsWith("./", StringComparison.Ordinal)
                        ? entryName[2..]
                        : entryName;

                if (string.Equals(
                        rootRelative,
                        "InitiativeTracker.Web",
                        StringComparison.Ordinal))
                {
                    foundApplicationExecutable = true;
                }
            }
        }
        catch (Exception ex)
            when (ex is InvalidDataException
                  or IOException)
        {
            errors.Add(
                $"Release archive could not be inspected: {ex.Message}");

            return errors;
        }

        if (!foundAnyEntry)
        {
            errors.Add(
                "Release archive is empty.");
        }

        if (!foundApplicationExecutable)
        {
            errors.Add(
                "Release archive does not contain the InitiativeTracker.Web executable at its root.");
        }

        return errors;
    }

    private static string NormalizeEntryName(
        string value)
    {
        return value
            .Replace('\\', '/')
            .Trim();
    }

    private static bool IsUnsafeArchivePath(
        string entryName)
    {
        if (entryName.StartsWith(
                "/",
                StringComparison.Ordinal))
        {
            return true;
        }

        if (entryName.Length >= 2 &&
            char.IsLetter(entryName[0]) &&
            entryName[1] == ':')
        {
            return true;
        }

        var segments =
            entryName.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(
            segment =>
                string.Equals(
                    segment,
                    "..",
                    StringComparison.Ordinal));
    }
}

public sealed class ReleaseVerificationResult
{
    public bool IsValid { get; init; }

    public ReleaseManifest? Manifest { get; init; }

    public string? ActualSha256 { get; init; }

    public IReadOnlyList<string> Errors { get; init; } =
        [];

    public static ReleaseVerificationResult Succeeded(
        ReleaseManifest manifest,
        string actualSha256)
    {
        return new ReleaseVerificationResult
        {
            IsValid = true,
            Manifest = manifest,
            ActualSha256 = actualSha256
        };
    }

    public static ReleaseVerificationResult Failed(
        IReadOnlyList<string> errors,
        ReleaseManifest? manifest = null,
        string? actualSha256 = null)
    {
        return new ReleaseVerificationResult
        {
            IsValid = false,
            Manifest = manifest,
            ActualSha256 = actualSha256,
            Errors = errors.ToList()
        };
    }
}
