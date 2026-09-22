using InitiativeTracker.Core.Models;
using InitiativeTracker.Core.Enums;
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

    [BindProperty]
    public int Quantity { get; set; } = 1;

    public bool CanUndo => _encounterState.CanUndo;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAddCombatant()
    {
        if (string.IsNullOrWhiteSpace(
            CombatantName))
        {
            ModelState.AddModelError(
                nameof(CombatantName),
                "Name is required.");

            return Page();
        }

        if (Quantity < 1 ||
            Quantity > 50)
        {
            ModelState.AddModelError(
                nameof(Quantity),
                "Quantity must be between 1 and 50.");

            return Page();
        }

        try
        {
            _encounterState.AddCombatants(
                CombatantName,
                Initiative,
                CurrentHp,
                MaxHp,
                Quantity);

            await BroadcastEncounterAsync();
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or InvalidOperationException)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateCombatant(
        Guid id,
        string name,
        int initiative,
        int? currentHp,
        int? maxHp)
    {
        try
        {
            _encounterState.UpdateCombatant(
                id,
                name,
                initiative,
                currentHp,
                maxHp);

            await BroadcastEncounterAsync();
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or InvalidOperationException)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteCombatant(
        Guid id)
    {
        try
        {
            _encounterState.DeleteCombatant(id);

            await BroadcastEncounterAsync();
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or InvalidOperationException)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            return Page();
        }

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

    public async Task<IActionResult> OnPostAdjustHp( Guid id, int amount)
    {
        try
        {
            _encounterState.AdjustHp(
                id,
                amount);

            await BroadcastEncounterAsync();
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or InvalidOperationException)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetHp( Guid id, int? currentHp)
    {
        try
        {
            _encounterState.SetCurrentHp(
                id,
                currentHp);

            await BroadcastEncounterAsync();
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDefeat( Guid id)
    {
        _encounterState.SetCombatantStatus(
            id,
            CombatantStatus.Defeated);

        await BroadcastEncounterAsync();

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemove( Guid id)
    {
        _encounterState.SetCombatantStatus(
            id,
            CombatantStatus.Removed);

        await BroadcastEncounterAsync();

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRestore( Guid id)
    {
        _encounterState.SetCombatantStatus(
            id,
            CombatantStatus.Active);

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

    public async Task<IActionResult> OnPostPreviousTurn()
    {
        try
        {
            _encounterState.PreviousTurn();

            await BroadcastEncounterAsync();
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUndo()
    {
        if (_encounterState.Undo())
        {
            await BroadcastEncounterAsync();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReorder(
        List<Guid> orderedIds)
    {
        try
        {
            _encounterState.ReorderCombatants(
                orderedIds);

            await BroadcastEncounterAsync();

            return new JsonResult(new
            {
                success = true
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    public async Task<IActionResult> OnPostMoveCombatant(
        Guid id,
        int direction)
    {
        try
        {
            _encounterState.MoveCombatant(
                id,
                direction);

            await BroadcastEncounterAsync();
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or ArgumentOutOfRangeException)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            return Page();
        }

        return RedirectToPage();
    }
}