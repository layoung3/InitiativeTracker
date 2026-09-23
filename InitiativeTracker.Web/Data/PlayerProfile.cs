namespace InitiativeTracker.Web.Data;

public class PlayerProfile
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public string Name { get; set; } =
        string.Empty;

    public string AccessToken { get; set; } =
        string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}