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

    public async Task<IActionResult> OnGetAsync(
        string? token)
    {
        if (!_gameSession.ValidateDisplayToken(
            token))
        {
            return Unauthorized();
        }

        await DeviceAuthenticationService
            .SignInAsync(
                HttpContext,
                _gameSession.CurrentSession,
                "Display");

        return RedirectToPage(
            "/Display/Index");
    }
}