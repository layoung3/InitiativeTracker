using InitiativeTracker.Web.Data.Enums;

namespace InitiativeTracker.Web.Data;

public class PlayerCharacterSpell
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid PlayerCharacterId { get; set; }

    public string Name { get; set; } =
        string.Empty;

    public int Level { get; set; }

    public string? School { get; set; }

    public string? CastingTime { get; set; }

    public string? Range { get; set; }

    public string? Area { get; set; }

    public string? Duration { get; set; }

    public bool HasVerbalComponent { get; set; }

    public bool HasSomaticComponent { get; set; }

    public bool HasMaterialComponent { get; set; }

    public string? MaterialDescription { get; set; }

    public bool RequiresConcentration { get; set; }

    public bool IsRitual { get; set; }

    public bool IsPrepared { get; set; } =
        true;

    public bool HasSpellAttackRoll { get; set; }

    public CharacterAbilityType? SavingThrowAbility
    {
        get;
        set;
    }

    public string? Notes { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public PlayerCharacter PlayerCharacter
    {
        get;
        set;
    } = null!;

    public ICollection<PlayerCharacterSpellEffectComponent> EffectComponents
    {
        get;
        set;
    } = new List<PlayerCharacterSpellEffectComponent>();
}