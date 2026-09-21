using InitiativeTracker.Core.Models;
using InitiativeTracker.Web.Models;

namespace InitiativeTracker.Web.Services;

public class EncounterStateService
{
    private readonly object _lock = new();

    public Encounter CurrentEncounter { get; private set; } = new()
    {
        Name = "Current Encounter"
    };

    public void AddCombatant(string name, int initiative, int? currentHp, int? maxHp)
    {
        lock (_lock)
        {
            CurrentEncounter.AddCombatant(new Combatant
            {
                Name = name.Trim(),
                Initiative = initiative,
                CurrentHp = currentHp,
                MaxHp = maxHp
            });
        }
    }

    public void StartCombat()
    {
        lock (_lock)
        {
            CurrentEncounter.StartCombat();
        }
    }

    public void AdvanceTurn()
    {
        lock (_lock)
        {
            CurrentEncounter.AdvanceTurn();
        }
    }

    public void ResetEncounter()
    {
        lock (_lock)
        {
            CurrentEncounter = new Encounter
            {
                Name = "Current Encounter"
            };
        }
    }

    public EncounterDisplayState GetDisplayState()
    {
        lock (_lock)
        {
            var orderedCombatants = CurrentEncounter.IsStarted
                ? CurrentEncounter.Combatants
                    .OrderBy(c => c.SortOrder)
                    .ToList()
                : CurrentEncounter.Combatants
                    .OrderByDescending(c => c.Initiative)
                    .ThenBy(c => c.SortOrder)
                    .ToList();

            var activeCombatants = orderedCombatants
                .Where(c => c.IsActive)
                .ToList();

            var currentTurnNumber = 0;

            if (CurrentEncounter.CurrentCombatantId.HasValue)
            {
                currentTurnNumber =
                    activeCombatants.FindIndex(
                        c => c.Id == CurrentEncounter.CurrentCombatantId.Value)
                    + 1;
            }

            return new EncounterDisplayState
            {
                IsStarted = CurrentEncounter.IsStarted,
                Round = CurrentEncounter.Round,
                CurrentTurnNumber = currentTurnNumber,
                ActiveCombatantCount = activeCombatants.Count,

                Combatants = orderedCombatants
                    .Select(c => new CombatantDisplayState
                    {
                        Id = c.Id,
                        Name = c.Name,
                        Initiative = c.Initiative,
                        CurrentHp = c.CurrentHp,
                        MaxHp = c.MaxHp,
                        IsActive = c.IsActive,
                        IsCurrent = CurrentEncounter.CurrentCombatantId == c.Id
                    })
                    .ToList()
            };
        }
    }
}