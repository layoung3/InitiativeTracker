using InitiativeTracker.Core.Enums;

namespace InitiativeTracker.Core.Models;

public class Encounter
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public int Round { get; private set; }

    public bool IsStarted { get; private set; }

    public Guid? CurrentCombatantId { get; private set; }

    public List<Combatant> Combatants { get; private set; } = [];

    public Combatant? CurrentCombatant =>
        CurrentCombatantId.HasValue
            ? Combatants.FirstOrDefault(c => c.Id == CurrentCombatantId.Value)
            : null;

    public int ActiveCombatantCount =>
        Combatants.Count(c => c.IsActive);

    public void AddCombatant(Combatant combatant)
    {
        ArgumentNullException.ThrowIfNull(combatant);

        if (string.IsNullOrWhiteSpace(combatant.Name))
        {
            throw new ArgumentException(
                "Combatant must have a name.",
                nameof(combatant));
        }

        combatant.SortOrder = Combatants.Count;

        Combatants.Add(combatant);
    }

    public void StartCombat()
    {
        var activeCombatants = Combatants
            .Where(c => c.IsActive)
            .OrderByDescending(c => c.Initiative)
            .ThenBy(c => c.SortOrder)
            .ToList();

        if (activeCombatants.Count == 0)
        {
            throw new InvalidOperationException(
                "Cannot start combat without an active combatant.");
        }

        for (var i = 0; i < activeCombatants.Count; i++)
        {
            activeCombatants[i].SortOrder = i;
        }

        Round = 1;
        IsStarted = true;
        CurrentCombatantId = activeCombatants[0].Id;
    }

    public void AdvanceTurn()
    {
        if (!IsStarted)
        {
            throw new InvalidOperationException(
                "Combat has not been started.");
        }

        var activeCombatants = Combatants
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ToList();

        if (activeCombatants.Count == 0)
        {
            throw new InvalidOperationException(
                "There are no active combatants remaining.");
        }

        var currentCombatant = CurrentCombatant;

        if (currentCombatant is null)
        {
            CurrentCombatantId = activeCombatants[0].Id;
            return;
        }

        var nextCombatant = activeCombatants
            .FirstOrDefault(c => c.SortOrder > currentCombatant.SortOrder);

        if (nextCombatant is not null)
        {
            CurrentCombatantId = nextCombatant.Id;
            return;
        }

        // If no active combatants remain after the current position, next round
        CurrentCombatantId = activeCombatants[0].Id;
        Round++;
    }
}