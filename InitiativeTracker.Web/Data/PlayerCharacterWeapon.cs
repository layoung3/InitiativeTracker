using InitiativeTracker.Web.Data.Enums;

namespace InitiativeTracker.Web.Data;

public class PlayerCharacterWeapon
{
    public Guid PlayerCharacterItemId { get; set; }

    public CharacterAbilityType AttackAbility
    {
        get;
        set;
    } = CharacterAbilityType.Strength;

    public bool UsesProficiency { get; set; } =
        true;

    public int AttackBonus { get; set; }

    public int PrimaryDamageBonus { get; set; }

    public int? NormalRange { get; set; }

    public int? LongRange { get; set; }

    public PlayerCharacterItem Item
    {
        get;
        set;
    } = null!;

    public ICollection<PlayerCharacterWeaponDamageComponent>
        DamageComponents
    {
        get;
        set;
    } = new List<PlayerCharacterWeaponDamageComponent>();
}