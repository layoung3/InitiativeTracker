using System.Text.RegularExpressions;

namespace InitiativeTracker.Web.Updates;

public static partial class
    ReleaseManifestValidator
{
    private const string ExpectedProduct =
        "InitiativeTracker";

    private const int SupportedFormatVersion =
        1;

    private static readonly HashSet<string>
        SupportedPlatforms =
        new(
            StringComparer.OrdinalIgnoreCase)
        {
            "linux-arm64"
        };


    public static IReadOnlyList<string>
        Validate(
            ReleaseManifest? manifest)
    {
        var errors =
            new List<string>();

        if (manifest is null)
        {
            errors.Add(
                "Release manifest is missing.");

            return errors;
        }


        if (manifest.FormatVersion !=
            SupportedFormatVersion)
        {
            errors.Add(
                $"Unsupported manifest format version: {manifest.FormatVersion}.");
        }


        if (!string.Equals(
                manifest.Product,
                ExpectedProduct,
                StringComparison.Ordinal))
        {
            errors.Add(
                $"Manifest product must be '{ExpectedProduct}'.");
        }


        if (string.IsNullOrWhiteSpace(
                manifest.Version) ||
            !SemanticVersionRegex()
                .IsMatch(
                    manifest.Version))
        {
            errors.Add(
                "Manifest version must be a semantic version such as 0.1.0 or 0.1.0-beta.1.");
        }


        if (string.IsNullOrWhiteSpace(
                manifest.Platform) ||
            !SupportedPlatforms.Contains(
                manifest.Platform))
        {
            errors.Add(
                $"Unsupported platform '{manifest.Platform}'.");
        }


        if (manifest.PublishedAtUtc ==
            default)
        {
            errors.Add(
                "PublishedAtUtc is required.");
        }


        if (manifest.HubProtocolVersion < 1)
        {
            errors.Add(
                "HubProtocolVersion must be at least 1.");
        }


        if (!string.Equals(
                manifest.HealthPath,
                "/system/health",
                StringComparison.Ordinal))
        {
            errors.Add(
                "HealthPath must be '/system/health' for manifest format 1.");
        }


        ValidatePackage(
            manifest.Package,
            errors);


        return errors;
    }


    public static bool IsValid(
        ReleaseManifest? manifest)
    {
        return Validate(
            manifest)
            .Count == 0;
    }


    private static void ValidatePackage(
        ReleasePackageManifest? package,
        List<string> errors)
    {
        if (package is null)
        {
            errors.Add(
                "Package information is required.");

            return;
        }


        if (string.IsNullOrWhiteSpace(
                package.FileName))
        {
            errors.Add(
                "Package file name is required.");
        }
        else
        {
            var fileName =
                package.FileName.Trim();

            if (!string.Equals(
                    fileName,
                    Path.GetFileName(
                        fileName),
                    StringComparison.Ordinal) ||
                fileName.Contains(
                    "..",
                    StringComparison.Ordinal))
            {
                errors.Add(
                    "Package file name must not contain a path.");
            }
        }


        if (package.SizeBytes <= 0)
        {
            errors.Add(
                "Package size must be greater than zero.");
        }


        if (string.IsNullOrWhiteSpace(
                package.Sha256) ||
            !Sha256Regex()
                .IsMatch(
                    package.Sha256))
        {
            errors.Add(
                "Package SHA-256 must contain exactly 64 hexadecimal characters.");
        }
    }


    [GeneratedRegex(
        @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex
        SemanticVersionRegex();


    [GeneratedRegex(
        @"^[0-9A-Fa-f]{64}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex
        Sha256Regex();
}
