namespace InitiativeTracker.Web.Models;

public class ConnectedPlayerOption
{
    public Guid PlayerProfileId { get; set; }

    public string PlayerName { get; set; } =
        string.Empty;
}