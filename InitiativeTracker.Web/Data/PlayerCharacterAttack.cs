using InitiativeTracker.Web.Data.Enums;

namespace InitiativeTracker.Web.Data;

public class PlayerCharacterAttack
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid PlayerCharacterId { get; set; }

    public string Name { get; set; } =
        string.Empty;

    public CharacterAbilityType AttackAbility
    {
        get;
        set;
    } = CharacterAbilityType.Strength;

    public bool AddsAbilityModifierToHit
    {
        get;
        set;
    } = true;

    public bool UsesProficiency { get; set; } =
        true;

    public int AttackBonus { get; set; }

    public int? NormalRange { get; set; }

    public int? LongRange { get; set; }

    public bool IsEnabled { get; set; } =
        true;

    public int SortOrder { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public PlayerCharacter PlayerCharacter
    {
        get;
        set;
    } = null!;

    public ICollection<PlayerCharacterAttackDamageComponent>
        DamageComponents
    {
        get;
        set;
    } = new List<PlayerCharacterAttackDamageComponent>();
}