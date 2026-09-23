namespace InitiativeTracker.Web.Data;

public class PlayerConnection
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid SessionId { get; set; }

    public string Name { get; set; } =
        string.Empty;

    public bool IsActive { get; set; } =
        true;

    public DateTime JoinedAtUtc { get; set; }

    public DateTime? LastSeenAtUtc { get; set; }

    public Guid PlayerProfileId { get; set; }
}