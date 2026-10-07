namespace InitiativeTracker.Web.Data;

public class ApplicationSettingsRecord
{
    public int Id { get; set; }

    public string DisplayTheme { get; set; } =
        "dark";

    /*
     * Stable identity for this physical/logical InitiativeTracker hub.
     * It is unrelated to the current game session and must survive
     * application restarts, software updates, and IP address changes.
     */
    public Guid HubId { get; set; }

    /*
     * DNS-safe hostname without the .local suffix.
     * Example: initiative-a71c3f2b
     */
    public string? HubHostName { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
