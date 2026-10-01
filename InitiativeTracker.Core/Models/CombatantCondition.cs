namespace InitiativeTracker.Core.Models;

public class CombatantCondition
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public string Name { get; set; } =
        string.Empty;

    public bool IsCustom { get; set; }

    public string? Notes { get; set; }
}