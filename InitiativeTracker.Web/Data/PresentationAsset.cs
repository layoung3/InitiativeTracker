namespace InitiativeTracker.Web.Data;

public sealed class PresentationAsset
{
    public string Scope { get; set; } = "";
    public Guid SubjectId { get; set; }
    public string Kind { get; set; } = "";
    public Guid BlobId { get; set; }
    public Guid? OwnerProfileId { get; set; }
    public long Length { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class PresentationSettings
{
    public int Id { get; set; } = 1;
    public string ApplicationName { get; set; } = "Initiative Tracker";
    public bool ShowApplicationLogo { get; set; } = true;
}
