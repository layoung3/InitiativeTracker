namespace InitiativeTracker.Web.Data;

public class GameSessionRecord
{
    public int Id { get; set; }

    public Guid SessionId { get; set; }

    public string JoinCode { get; set; } = string.Empty;

    public string DmPin { get; set; } = string.Empty;

    public string PlayerJoinToken { get; set; } = string.Empty;

    public string DisplayToken { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}