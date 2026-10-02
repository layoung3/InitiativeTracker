namespace InitiativeTracker.Web.Rules.Dnd5e;

public class Dnd5eAttackDamageView
{
    public string DamageExpression { get; init; } =
        string.Empty;

    public string DamageType { get; init; } =
        string.Empty;

    public int FlatBonus { get; init; }

    public bool IsPrimary { get; init; }

    public string? VersatileDamageExpression
    {
        get;
        init;
    }
}