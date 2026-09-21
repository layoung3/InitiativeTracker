using InitiativeTracker.Core.Models;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using InitiativeTracker.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace InitiativeTracker.Web.Pages.Dm;

public class IndexModel : PageModel
{
    private readonly EncounterStateService _encounterState;
    private readonly IHubContext<EncounterHub> _encounterHub;

    public IndexModel(EncounterStateService encounterState, IHubContext<EncounterHub> encounterHub)
    {
        _encounterState = encounterState;
        _encounterHub = encounterHub;
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

    public async Task<IActionResult> OnPostAddCombatant()
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

        await BroadcastEncounterAsync();

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostStartCombat()
    {
        try
        {
            _encounterState.StartCombat();
            await BroadcastEncounterAsync();
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostNextTurn()
    {
        try
        {
            _encounterState.AdvanceTurn();
            await BroadcastEncounterAsync();
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReset()
    {
        _encounterState.ResetEncounter();

        await BroadcastEncounterAsync();

        return RedirectToPage();
    }

    private async Task BroadcastEncounterAsync()
    {
        var displayState =
            _encounterState.GetDisplayState();

        await _encounterHub.Clients.All.SendAsync(
            "EncounterUpdated",
            displayState);
    }
}