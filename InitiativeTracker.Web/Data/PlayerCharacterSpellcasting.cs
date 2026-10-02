using InitiativeTracker.Web.Data.Enums;

namespace InitiativeTracker.Web.Data;

public class PlayerCharacterSpellcasting
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid PlayerCharacterId { get; set; }

    public CharacterAbilityType SpellcastingAbility
    {
        get;
        set;
    } = CharacterAbilityType.Intelligence;

    public int AdditionalSpellAttackBonus
    {
        get;
        set;
    }

    public int AdditionalSpellSaveDcBonus
    {
        get;
        set;
    }

    /*
     * The one spell this character is currently
     * concentrating on, if any.
     *
     * We intentionally keep this as an ID rather than a
     * navigation property so deleting/editing spells can be
     * handled explicitly by CharacterService.
     */
    public Guid? ActiveConcentrationSpellId
    {
        get;
        set;
    }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public PlayerCharacter PlayerCharacter
    {
        get;
        set;
    } = null!;

    public ICollection<PlayerCharacterSpellSlot>
        SpellSlots
    {
        get;
        set;
    } = new List<PlayerCharacterSpellSlot>();
}