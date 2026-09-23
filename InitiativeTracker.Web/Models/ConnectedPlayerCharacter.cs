namespace InitiativeTracker.Web.Models;

public class ConnectedPlayerCharacter
{
    public Guid PlayerProfileId { get; set; }

    public string PlayerName { get; set; } =
        string.Empty;

    public Guid CharacterId { get; set; }

    public string CharacterName { get; set; } =
        string.Empty;

    public int Level { get; set; }

    public int CurrentHp { get; set; }

    public int MaxHp { get; set; }

    public bool AlreadyAdded { get; set; }
}