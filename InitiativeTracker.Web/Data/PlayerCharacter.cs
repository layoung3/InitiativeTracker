namespace InitiativeTracker.Web.Data;

public class PlayerCharacter
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid PlayerProfileId { get; set; }

    public string Name { get; set; } =
        string.Empty;

    public string? ImagePath { get; set; }

    public int Level { get; set; } = 1;

    public int ProficiencyBonus { get; set; } = 2;

    public int MovementSpeed { get; set; } = 30;

    public int CurrentHp { get; set; }

    public int MaxHp { get; set; }

    public int TemporaryHp { get; set; }

    public bool Inspiration { get; set; }

    public int ExhaustionLevel { get; set; }

    public int Strength { get; set; } = 10;

    public int Dexterity { get; set; } = 10;

    public int Constitution { get; set; } = 10;

    public int Intelligence { get; set; } = 10;

    public int Wisdom { get; set; } = 10;

    public int Charisma { get; set; } = 10;

    public bool StrengthSaveProficient { get; set; }

    public bool DexteritySaveProficient { get; set; }

    public bool ConstitutionSaveProficient { get; set; }

    public bool IntelligenceSaveProficient { get; set; }

    public bool WisdomSaveProficient { get; set; }

    public bool CharismaSaveProficient { get; set; }

    public ICollection<PlayerCharacterSkill> Skills{ get; set;} = new List<PlayerCharacterSkill>();

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}