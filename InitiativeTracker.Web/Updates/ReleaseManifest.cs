namespace InitiativeTracker.Web.Updates;

public sealed class ReleaseManifest
{
    public int FormatVersion { get; init; } =
        1;

    public string Product { get; init; } =
        "InitiativeTracker";

    public string Version { get; init; } =
        string.Empty;

    public string Platform { get; init; } =
        string.Empty;

    public DateTimeOffset PublishedAtUtc { get; init; }

    public int HubProtocolVersion { get; init; } =
        1;

    public string HealthPath { get; init; } =
        "/system/health";

    public bool RequiresDatabaseBackup { get; init; } =
        true;

    public ReleasePackageManifest Package { get; init; } =
        new();

    public IReadOnlyList<string> ReleaseNotes { get; init; } =
        [];
}


public sealed class ReleasePackageManifest
{
    public string FileName { get; init; } =
        string.Empty;

    public long SizeBytes { get; init; }

    public string Sha256 { get; init; } =
        string.Empty;
}
