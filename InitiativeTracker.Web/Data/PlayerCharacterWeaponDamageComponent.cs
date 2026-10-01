namespace InitiativeTracker.Web.Data;

public class PlayerCharacterWeaponDamageComponent
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid PlayerCharacterItemId { get; set; }

    public string DamageDice { get; set; } =
        string.Empty;

    public string DamageType { get; set; } =
        string.Empty;

    public bool IsPrimary { get; set; }

    public bool AddsAbilityModifier { get; set; }

    public string? VersatileDamageDice { get; set; }

    public int SortOrder { get; set; }

    public PlayerCharacterWeapon Weapon
    {
        get;
        set;
    } = null!;
}