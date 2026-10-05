namespace InitiativeTracker.Web.Data;

public class ApplicationSettingsRecord
{
    public int Id { get; set; }

    public string DisplayTheme { get; set; } = "dark";

    public DateTime UpdatedAtUtc { get; set; }
}
