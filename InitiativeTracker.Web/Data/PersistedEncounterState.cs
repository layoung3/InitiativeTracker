namespace InitiativeTracker.Web.Data;

public class PersistedEncounterState
{
    public int Id { get; set; }

    public string EncounterJson { get; set; } = string.Empty;

    public string UndoHistoryJson { get; set; } = "[]";

    public DateTime UpdatedAtUtc { get; set; }
}