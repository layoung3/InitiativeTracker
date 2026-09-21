using InitiativeTracker.Core.Models;

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
}