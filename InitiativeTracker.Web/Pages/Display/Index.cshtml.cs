using InitiativeTracker.Web.Models;
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

    public EncounterDisplayState DisplayState { get; private set; } = new();

    public void OnGet()
    {
        DisplayState = _encounterState.GetDisplayState();
    }
}