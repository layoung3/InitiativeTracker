using InitiativeTracker.Core.Enums;
using InitiativeTracker.Core.Models;
using InitiativeTracker.Web.Models;

namespace InitiativeTracker.Web.Services;

public class EncounterStateService
{
    private readonly object _lock = new();

    private const int MaxUndoHistory = 50;

    private readonly List<EncounterSnapshot> _undoHistory = [];

    public Encounter CurrentEncounter { get; private set; } = new()
    {
        Name = "Current Encounter"
    };

    public bool CanUndo
    {
        get
        {
            lock (_lock)
            {
                return _undoHistory.Count > 0;
            }
        }
    }

    private void ExecuteMutation(
        Action mutation)
    {
        lock (_lock)
        {
            var snapshot =
                CurrentEncounter.CreateSnapshot();

            // If this throws, the snapshot is never added.
            mutation();

            _undoHistory.Add(snapshot);

            if (_undoHistory.Count > MaxUndoHistory)
            {
                _undoHistory.RemoveAt(0);
            }
        }
    }

    public void AddCombatants(
        string name,
        int initiative,
        int? currentHp,
        int? maxHp,
        int quantity)
    {
        if (quantity < 1 ||
            quantity > 50)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Quantity must be between 1 and 50.");
        }

        ExecuteMutation(() =>
        {
            var baseName =
                name.Trim();

            for (var i = 0; i < quantity; i++)
            {
                var combatantName =
                    quantity == 1
                        ? baseName
                        : $"{baseName} {i + 1}";

                CurrentEncounter.AddCombatant(
                    new Combatant
                    {
                        Name = combatantName,
                        Initiative = initiative,
                        CurrentHp = currentHp,
                        MaxHp = maxHp
                    });
            }
        });
    }

    public void UpdateCombatant(
        Guid combatantId,
        string name,
        int initiative,
        int? currentHp,
        int? maxHp)
    {
        ExecuteMutation(() =>
            CurrentEncounter.UpdateCombatant(
                combatantId,
                name,
                initiative,
                currentHp,
                maxHp));
    }

    public void DeleteCombatant(
        Guid combatantId)
    {
        ExecuteMutation(() =>
            CurrentEncounter.DeleteCombatant(
                combatantId));
    }

    public void ReorderCombatants(
        IReadOnlyList<Guid> orderedIds)
    {
        ExecuteMutation(() =>
            CurrentEncounter.ReorderCombatants(
                orderedIds));
    }

    public void MoveCombatant(
        Guid combatantId,
        int direction)
    {
        ExecuteMutation(() =>
            CurrentEncounter.MoveCombatant(
                combatantId,
                direction));
    }

    public void StartCombat()
    {
        ExecuteMutation(
            CurrentEncounter.StartCombat);
    }

    public void AdvanceTurn()
    {
        ExecuteMutation(
            CurrentEncounter.AdvanceTurn);
    }

    public void PreviousTurn()
    {
        ExecuteMutation(
            CurrentEncounter.PreviousTurn);
    }

    public void ResetEncounter()
    {
        ExecuteMutation(() =>
        {
            CurrentEncounter = new Encounter
            {
                Name = "Current Encounter"
            };
        });
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
        ExecuteMutation(() =>
            CurrentEncounter.AdjustHp(
                combatantId,
                amount));
    }

    public void SetCurrentHp(
        Guid combatantId,
        int? currentHp)
    {
        ExecuteMutation(() =>
            CurrentEncounter.SetCurrentHp(
                combatantId,
                currentHp));
    }

    public void SetCombatantStatus(
        Guid combatantId,
        CombatantStatus status)
    {
        ExecuteMutation(() =>
            CurrentEncounter.SetCombatantStatus(
                combatantId,
                status));
    }

    public bool Undo()
    {
        lock (_lock)
        {
            if (_undoHistory.Count == 0)
            {
                return false;
            }

            var lastIndex =
                _undoHistory.Count - 1;

            var snapshot =
                _undoHistory[lastIndex];

            _undoHistory.RemoveAt(lastIndex);

            CurrentEncounter =
                Encounter.FromSnapshot(snapshot);

            return true;
        }
    }

}