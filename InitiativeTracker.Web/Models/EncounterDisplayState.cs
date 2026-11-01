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
    public string PortraitUrl { get; set; } = Services.MediaPaths.DefaultPortrait;
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int Initiative { get; set; }

    public int ExhaustionLevel { get; set; }

    public int? CurrentHp { get; set; }

    public int? MaxHp { get; set; }

    public int? TemporaryHp { get; set; }

    public bool IsActive { get; set; }

    public bool IsCurrent { get; set; }

    public List<ConditionDisplayState> Conditions { get; set; } = [];

    public class ConditionDisplayState
    {
        public string Name { get; set; } = string.Empty;

        public string StyleKey { get; set; } = "custom";
    }
}
