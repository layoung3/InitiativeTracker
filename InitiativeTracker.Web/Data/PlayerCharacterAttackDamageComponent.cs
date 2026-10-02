namespace InitiativeTracker.Web.Data;

public class PlayerCharacterAttackDamageComponent
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid PlayerCharacterAttackId { get; set; }

    public string DamageExpression { get; set; } =
        string.Empty;

    public string DamageType { get; set; } =
        string.Empty;

    public bool AddsAbilityModifier { get; set; }

    public int AdditionalDamageBonus { get; set; }

    public int SortOrder { get; set; }

    public PlayerCharacterAttack Attack
    {
        get;
        set;
    } = null!;
}