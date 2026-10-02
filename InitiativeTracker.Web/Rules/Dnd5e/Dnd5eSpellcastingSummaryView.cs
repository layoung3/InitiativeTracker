using InitiativeTracker.Web.Data.Enums;

namespace InitiativeTracker.Web.Rules.Dnd5e;

public class Dnd5eSpellcastingSummaryView
{
    public CharacterAbilityType Ability
    {
        get;
        init;
    }

    public int SpellAttackBonus { get; init; }

    public int SpellSaveDc { get; init; }
}