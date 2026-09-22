using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.Join;

[AllowAnonymous]
public class IndexModel : PageModel
{
    private readonly GameSessionService
        _gameSession;

    public IndexModel(
        GameSessionService gameSession)
    {
        _gameSession = gameSession;
    }

    [BindProperty]
    public string JoinCode { get; set; } =
        string.Empty;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!_gameSession.ValidateJoinCode(
            JoinCode))
        {
            ModelState.AddModelError(
                nameof(JoinCode),
                "Game code was not found.");

            return Page();
        }

        await DeviceAuthenticationService
            .SignInAsync(
                HttpContext,
                _gameSession.CurrentSession,
                "Player");

        return RedirectToPage(
            "/Join/Success");
    }
}