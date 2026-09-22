using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;

namespace InitiativeTracker.Web.Pages.Dm;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly GameSessionService
        _gameSession;

    public LoginModel(
        GameSessionService gameSession)
    {
        _gameSession = gameSession;
    }

    [BindProperty]
    public string Pin { get; set; } =
        string.Empty;

    public IActionResult OnGet()
    {
        if (User.IsInRole("DM"))
        {
            return RedirectToPage(
                "/Dm/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!_gameSession.ValidateDmPin(
            Pin))
        {
            ModelState.AddModelError(
                nameof(Pin),
                "Invalid DM PIN.");

            return Page();
        }

        await DeviceAuthenticationService
            .SignInAsync(
                HttpContext,
                _gameSession.CurrentSession,
                "DM");

        return RedirectToPage(
            "/Dm/Index");
    }
}