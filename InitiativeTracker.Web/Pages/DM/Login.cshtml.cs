using InitiativeTracker.Web.Services;
using InitiativeTracker.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;

namespace InitiativeTracker.Web.Pages.Dm;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly GameSessionService _gameSession;
    private readonly ITrustedBrowserOrigin _secureOrigin;

    public LoginModel(GameSessionService gameSession, ITrustedBrowserOrigin secureOrigin)
    {
        _gameSession = gameSession;
        _secureOrigin = secureOrigin;
    }

    [BindProperty]
    public string Pin { get; set; } =
        string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.IsInRole("DM"))
        {
            if (!_secureOrigin.TrustedRequest(HttpContext))
                return RedirectToPage("/Dm/Index");
            if (await SecureAccess.Current(HttpContext) is { Identity.Role: "DM" })
                return RedirectToPage("/Dm/Index");
            // Revoked or expired elevated cookies need primary credentials
            // again. A newly verified PIN cookie can continue to approval.
            if (User.HasClaim(c => c.Type == SecureAccess.FreshLoginClaim))
                return RedirectToPage("/SecureAccess/Wait", new { start = true });
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

        // Ordinary PIN login is not elevated authority on its own. The HTTPS
        // browser automatically requests fresh physical authorization or resumes
        // remembered trust on a dedicated waiting page.
        return _secureOrigin.TrustedRequest(HttpContext)
            ? RedirectToPage("/SecureAccess/Wait", new { start = true })
            : RedirectToPage("/Dm/Index");
    }
}