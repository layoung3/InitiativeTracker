using InitiativeTracker.Core.Enums;
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
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.SortOrder)
                    .ToList()
                : CurrentEncounter.Combatants
                    .Where(c => c.IsActive)
                    .OrderByDescending(c => c.Initiative)
                    .ThenBy(c => c.SortOrder)
                    .ToList();

            var activeCombatants = orderedCombatants;

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

    public void AdjustHp(
    Guid combatantId,
    int amount)
    {
        lock (_lock)
        {
            CurrentEncounter.AdjustHp(
                combatantId,
                amount);
        }
    }

    public void SetCurrentHp(
        Guid combatantId,
        int? currentHp)
    {
        lock (_lock)
        {
            CurrentEncounter.SetCurrentHp(
                combatantId,
                currentHp);
        }
    }

    public void SetCombatantStatus(
        Guid combatantId,
        CombatantStatus status)
    {
        lock (_lock)
        {
            CurrentEncounter.SetCombatantStatus(
                combatantId,
                status);
        }
    }
}