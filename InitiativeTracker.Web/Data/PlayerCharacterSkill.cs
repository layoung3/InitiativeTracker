using InitiativeTracker.Web.Data.Enums;

namespace InitiativeTracker.Web.Data;

public class PlayerCharacterSkill
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid PlayerCharacterId { get; set; }

    public CharacterSkillType Skill { get; set; }

    public SkillProficiencyLevel ProficiencyLevel
    {
        get;
        set;
    }

    /// <summary>
    /// Manual additive adjustment applied after
    /// ability and proficiency calculations.
    ///
    /// Examples:
    /// +1 from a custom feature
    /// -2 from a curse
    ///
    /// Item/equipment bonuses can eventually be
    /// calculated separately rather than permanently
    /// written here.
    /// </summary>
    public int AdditionalBonus { get; set; }

    /// <summary>
    /// Optional hard override for the final skill bonus.
    ///
    /// When set, this replaces the normal calculated
    /// value entirely.
    /// </summary>
    public int? OverrideBonus { get; set; }

    public PlayerCharacter PlayerCharacter
    {
        get;
        set;
    } = null!;
}