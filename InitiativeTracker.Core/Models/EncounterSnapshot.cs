using InitiativeTracker.Core.Enums;

namespace InitiativeTracker.Core.Models;

public class EncounterSnapshot
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Round { get; set; }

    public bool IsStarted { get; set; }

    public Guid? CurrentCombatantId { get; set; }

    public List<CombatantSnapshot> Combatants { get; set; } = [];
}

public class CombatantSnapshot
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Initiative { get; set; }

    public int SortOrder { get; set; }

    public int? CurrentHp { get; set; }

    public int? MaxHp { get; set; }

    public int TemporaryHp { get; set; }

    public CombatantStatus Status { get; set; }

    public Guid? PlayerCharacterId { get; set; }

    public Guid? AssignedPlayerProfileId { get; set; }

    public bool IsHiddenFromPublic { get; set; }

    public bool IsHpHiddenFromPublic { get; set; }
}