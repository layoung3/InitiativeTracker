namespace InitiativeTracker.Web.Data;

public class PlayerCharacterCondition
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid PlayerCharacterId { get; set; }

    public string Name { get; set; } =
        string.Empty;

    public bool IsCustom { get; set; }

    public string? Notes { get; set; }

    public PlayerCharacter PlayerCharacter { get; set; } = null!;
}