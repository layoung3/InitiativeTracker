using InitiativeTracker.Core.Enums;

namespace InitiativeTracker.Core.Models;

public class Combatant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public int Initiative { get; set; }

    public int ExhaustionLevel { get; set; }

    /// <summary>
    /// Represents the combatant's explicit position in the initiative order.
    /// Lower numbers act earlier.
    /// </summary>
    public int SortOrder { get; set; }

    public int? CurrentHp { get; set; }

    public int? MaxHp { get; set; }

    public int TemporaryHp { get; set; }

    public CombatantStatus Status { get; set; } = CombatantStatus.Active;

    public bool IsActive => Status == CombatantStatus.Active;

    public Guid? PlayerCharacterId { get; set; }

    public Guid? AssignedPlayerProfileId { get; set; }

    public bool IsHiddenFromPublic { get; set; }

    public bool IsHpHiddenFromPublic { get; set; }

    public List<CombatantCondition> Conditions { get; set; } = [];
}