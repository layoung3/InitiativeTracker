using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.Display;

[AllowAnonymous]
public class ConnectModel : PageModel
{
    private readonly GameSessionService
        _gameSession;

    public ConnectModel(
        GameSessionService gameSession)
    {
        _gameSession = gameSession;
    }

    public IActionResult OnGet(string? token)
    {
        if (!_gameSession.ValidateDisplayToken(token))
            return Unauthorized();

        // QR is an invitation to pair, not proof of a trusted registered device.
        // Issuing an unpaired Display cookie here is rejected by the existing
        // cookie validator; never weaken that validation to make the QR work.
        return RedirectToPage("/Display/Pair");
    }
}