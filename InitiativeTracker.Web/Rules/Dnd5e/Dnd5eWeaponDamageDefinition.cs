namespace InitiativeTracker.Web.Rules.Dnd5e;

public class Dnd5eWeaponDamageDefinition
{
    public string DamageDice { get; init; } =
        string.Empty;

    public string DamageType { get; init; } =
        string.Empty;

    public bool IsPrimary { get; init; }

    public bool AddsAbilityModifier { get; init; }

    public string? VersatileDamageDice { get; init; }

    public int SortOrder { get; init; }
}