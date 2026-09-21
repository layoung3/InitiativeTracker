using InitiativeTracker.Core.Models;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.Dm;

public class IndexModel : PageModel
{
    private readonly EncounterStateService _encounterState;

    public IndexModel(EncounterStateService encounterState)
    {
        _encounterState = encounterState;
    }

    public Encounter Encounter => _encounterState.CurrentEncounter;

    [BindProperty]
    public string CombatantName { get; set; } = string.Empty;

    [BindProperty]
    public int Initiative { get; set; }

    [BindProperty]
    public int? CurrentHp { get; set; }

    [BindProperty]
    public int? MaxHp { get; set; }

    public void OnGet()
    {
    }

    public IActionResult OnPostAddCombatant()
    {
        if (string.IsNullOrWhiteSpace(CombatantName))
        {
            ModelState.AddModelError(
                nameof(CombatantName),
                "Name is required.");

            return Page();
        }

        if (Encounter.IsStarted)
        {
            ModelState.AddModelError(
                string.Empty,
                "Adding combatants after combat starts is not supported yet.");

            return Page();
        }

        _encounterState.AddCombatant(
            CombatantName,
            Initiative,
            CurrentHp,
            MaxHp);

        return RedirectToPage();
    }

    public IActionResult OnPostStartCombat()
    {
        try
        {
            _encounterState.StartCombat();
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        return RedirectToPage();
    }

    public IActionResult OnPostNextTurn()
    {
        try
        {
            _encounterState.AdvanceTurn();
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        return RedirectToPage();
    }

    public IActionResult OnPostReset()
    {
        _encounterState.ResetEncounter();

        return RedirectToPage();
    }
}