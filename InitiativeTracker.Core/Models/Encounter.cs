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

    public Combatant GetCombatant(Guid combatantId)
    {
        return Combatants.FirstOrDefault(c => c.Id == combatantId)
            ?? throw new ArgumentException(
                "Combatant was not found.",
                nameof(combatantId));
    }

    public void AdjustHp(Guid combatantId, int amount)
    {
        var combatant = GetCombatant(combatantId);

        if (!combatant.CurrentHp.HasValue)
        {
            throw new InvalidOperationException(
                "Combatant does not currently have HP assigned.");
        }

        var newHp = combatant.CurrentHp.Value + amount;

        combatant.CurrentHp = ClampHp(
            newHp,
            combatant.MaxHp);
    }

    public void SetCurrentHp(Guid combatantId, int? currentHp)
    {
        var combatant = GetCombatant(combatantId);

        if (!currentHp.HasValue)
        {
            combatant.CurrentHp = null;
            return;
        }

        combatant.CurrentHp = ClampHp(
            currentHp.Value,
            combatant.MaxHp);
    }

    public void SetCombatantStatus(
        Guid combatantId,
        CombatantStatus status)
    {
        var combatant = GetCombatant(combatantId);

        var wasCurrent =
            CurrentCombatantId == combatant.Id;

        combatant.Status = status;

        if (IsStarted &&
            wasCurrent &&
            !combatant.IsActive)
        {
            MoveToNextActiveCombatant(
                combatant.SortOrder);
        }
    }

    private static int ClampHp(
        int hp,
        int? maxHp)
    {
        hp = Math.Max(0, hp);

        if (maxHp.HasValue)
        {
            hp = Math.Min(hp, maxHp.Value);
        }

        return hp;
    }

    private void MoveToNextActiveCombatant(
        int previousSortOrder)
    {
        var activeCombatants = Combatants
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ToList();

        if (activeCombatants.Count == 0)
        {
            CurrentCombatantId = null;
            return;
        }

        var nextCombatant = activeCombatants
            .FirstOrDefault(
                c => c.SortOrder > previousSortOrder);

        if (nextCombatant is not null)
        {
            CurrentCombatantId = nextCombatant.Id;
            return;
        }

        CurrentCombatantId =
            activeCombatants[0].Id;

        Round++;
    }

    public EncounterSnapshot CreateSnapshot()
    {
        return new EncounterSnapshot
        {
            Id = Id,
            Name = Name,
            Round = Round,
            IsStarted = IsStarted,
            CurrentCombatantId = CurrentCombatantId,

            Combatants = Combatants
                .Select(c => new CombatantSnapshot
                {
                    Id = c.Id,
                    Name = c.Name,
                    Initiative = c.Initiative,
                    SortOrder = c.SortOrder,
                    CurrentHp = c.CurrentHp,
                    MaxHp = c.MaxHp,
                    Status = c.Status
                })
                .ToList()
        };
    }

    public static Encounter FromSnapshot(
        EncounterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var encounter = new Encounter
        {
            Id = snapshot.Id,
            Name = snapshot.Name,
            Round = snapshot.Round,
            IsStarted = snapshot.IsStarted,
            CurrentCombatantId = snapshot.CurrentCombatantId
        };

        encounter.Combatants.AddRange(
            snapshot.Combatants.Select(c => new Combatant
            {
                Id = c.Id,
                Name = c.Name,
                Initiative = c.Initiative,
                SortOrder = c.SortOrder,
                CurrentHp = c.CurrentHp,
                MaxHp = c.MaxHp,
                Status = c.Status
            }));

        return encounter;
    }

    public void PreviousTurn()
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

        var currentIndex = activeCombatants.FindIndex(
            c => c.Id == currentCombatant.Id);

        if (currentIndex > 0)
        {
            CurrentCombatantId =
                activeCombatants[currentIndex - 1].Id;

            return;
        }

        // Don't allow going backward before Round 1.
        if (Round <= 1)
        {
            return;
        }

        Round--;

        CurrentCombatantId =
            activeCombatants[^1].Id;
    }
}