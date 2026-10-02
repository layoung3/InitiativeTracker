using InitiativeTracker.Web.Data.Enums;

namespace InitiativeTracker.Web.Data;

public class PlayerCharacterSpellEffectComponent
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid PlayerCharacterSpellId { get; set; }

    public SpellEffectType EffectType { get; set; }

    public string Expression { get; set; } =
        string.Empty;

    public string? DamageType { get; set; }

    public bool AddsSpellcastingAbilityModifier
    {
        get;
        set;
    }

    public int AdditionalBonus { get; set; }

    public string? UpcastExpressionPerLevel
    {
        get;
        set;
    }

    /*
     * Optional extra effect added at each cantrip scaling
     * threshold (character levels 5, 11, and 17).
     *
     * Example:
     * Base Expression: 1d10
     * CantripScalingExpression: 1d10
     */
    public string? CantripScalingExpression
    {
        get;
        set;
    }

    public int SortOrder { get; set; }

    public PlayerCharacterSpell Spell
    {
        get;
        set;
    } = null!;
}