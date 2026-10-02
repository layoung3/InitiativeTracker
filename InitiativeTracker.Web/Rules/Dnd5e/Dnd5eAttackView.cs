using InitiativeTracker.Web.Data.Enums;

namespace InitiativeTracker.Web.Rules.Dnd5e;

public class Dnd5eAttackView
{
    public Guid SourceId { get; init; }

    public Dnd5eAttackSourceType SourceType
    {
        get;
        init;
    }

    public string Name { get; init; } =
        string.Empty;

    public CharacterAbilityType AttackAbility
    {
        get;
        init;
    }

    public int ToHitBonus { get; init; }

    public int? NormalRange { get; init; }

    public int? LongRange { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyList<Dnd5eAttackDamageView>
        DamageComponents
    {
        get;
        init;
    } = [];
}