namespace InitiativeTracker.Web.Data;

public class PlayerCharacterSpellSlot
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid PlayerCharacterSpellcastingId
    {
        get;
        set;
    }

    public int Level { get; set; }

    public int MaximumSlots { get; set; }

    public int RemainingSlots { get; set; }

    public PlayerCharacterSpellcasting Spellcasting
    {
        get;
        set;
    } = null!;
}