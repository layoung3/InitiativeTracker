using InitiativeTracker.Core.Models;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.Display;

public class IndexModel : PageModel
{
    private readonly EncounterStateService _encounterState;

    public IndexModel(EncounterStateService encounterState)
    {
        _encounterState = encounterState;
    }

    public Encounter Encounter => _encounterState.CurrentEncounter;

    public IReadOnlyList<Combatant> OrderedCombatants =>
        Encounter.IsStarted
            ? Encounter.Combatants
                .OrderBy(c => c.SortOrder)
                .ToList()
            : Encounter.Combatants
                .OrderByDescending(c => c.Initiative)
                .ThenBy(c => c.SortOrder)
                .ToList();

    public int CurrentTurnNumber
    {
        get
        {
            if (!Encounter.IsStarted ||
                Encounter.CurrentCombatant is null)
            {
                return 0;
            }

            var active = OrderedCombatants
                .Where(c => c.IsActive)
                .ToList();

            return active.FindIndex(
                c => c.Id == Encounter.CurrentCombatant.Id) + 1;
        }
    }

    public void OnGet()
    {
    }
}