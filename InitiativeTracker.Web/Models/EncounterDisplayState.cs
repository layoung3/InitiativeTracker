namespace InitiativeTracker.Web.Models;

public class EncounterDisplayState
{
    public bool IsStarted { get; set; }

    public int Round { get; set; }

    public int CurrentTurnNumber { get; set; }

    public int ActiveCombatantCount { get; set; }

    public List<CombatantDisplayState> Combatants { get; set; } = [];
}

public class CombatantDisplayState
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Initiative { get; set; }

    public int? CurrentHp { get; set; }

    public int? MaxHp { get; set; }

    public int? TemporaryHp { get; set; }

    public bool IsActive { get; set; }

    public bool IsCurrent { get; set; }
}