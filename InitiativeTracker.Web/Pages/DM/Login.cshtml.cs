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
    private readonly DmAuthenticationPolicy _authentication;
    public bool CanSubmitPin => _authentication.PinSubmissionAllowed(HttpContext);
    public bool RequiresTrustedPin => _authentication.RequiresTrustedPin;
    public string? SecureLoginUrl => _authentication.BrowserOrigin is { } origin ? origin + "/dm/login" : null;

    public LoginModel(
        GameSessionService gameSession, DmAuthenticationPolicy authentication)
    {
        _gameSession = gameSession;
        _authentication = authentication;
    }

    [BindProperty]
    public string Pin { get; set; } =
        string.Empty;

    public IActionResult OnGet()
    {
        SensitiveHeaders();
        if (User.IsInRole("DM") && (!_authentication.RequiresTrustedPin || _authentication.SecureDm(HttpContext)))
        {
            return RedirectToPage(
                "/Dm/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        SensitiveHeaders();
        if (!CanSubmitPin) { Pin = ""; ModelState.Clear(); return StatusCode(403); }
        if (!_gameSession.ValidateDmPin(
            Pin))
        {
            Pin = "";
            ModelState.Remove(nameof(Pin));
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

    private void SensitiveHeaders()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
