using InitiativeTracker.Web.Data.Enums;

namespace InitiativeTracker.Web.Data;

public class PlayerCharacterItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PlayerCharacterId { get; set; }

    public string Name { get; set; } = string.Empty;

    public CharacterItemType ItemType { get; set; } = CharacterItemType.General;

    public int Quantity { get; set; } = 1;

    public bool IsEquipped { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public PlayerCharacter PlayerCharacter { get; set; } = null!;

    public PlayerCharacterWeapon? Weapon { get; set; }
}