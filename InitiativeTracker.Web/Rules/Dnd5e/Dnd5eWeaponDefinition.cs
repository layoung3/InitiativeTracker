using InitiativeTracker.Web.Data.Enums;

namespace InitiativeTracker.Web.Rules.Dnd5e;

public class Dnd5eWeaponDefinition
{
    public string Key { get; init; } =
        string.Empty;

    public string Name { get; init; } =
        string.Empty;

    public CharacterAbilityType DefaultAttackAbility
    {
        get;
        init;
    }

    public IReadOnlyList<CharacterAbilityType>
        AllowedAttackAbilities
    {
        get;
        init;
    } = [];

    public int? NormalRange { get; init; }

    public int? LongRange { get; init; }

    public IReadOnlyList<Dnd5eWeaponDamageDefinition>
        DamageComponents
    {
        get;
        init;
    } = [];
}