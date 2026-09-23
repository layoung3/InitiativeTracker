namespace InitiativeTracker.Web.Models;

public class PlayerProfileAdminViewModel
{
    public Guid ProfileId { get; set; }

    public string PlayerName { get; set; } =
        string.Empty;

    public DateTime UpdatedAtUtc { get; set; }

    public bool IsReferencedByEncounter { get; set; }

    public List<PlayerProfileCharacterAdminViewModel>
        Characters
    { get; set; } = [];
}

public class PlayerProfileCharacterAdminViewModel
{
    public Guid CharacterId { get; set; }

    public string CharacterName { get; set; } =
        string.Empty;

    public bool IsInEncounter { get; set; }
}