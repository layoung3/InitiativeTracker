using InitiativeTracker.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.SecureAccess;

// Role-level, trusted-HTTPS account controls. Never available on legacy HTTP
// or without an independently validated secure grant.
[AllowAnonymous]
[RequestSizeLimit(8192)]
[RequestFormLimits(ValueLengthLimit = 256, ValueCountLimit = 16)]
public sealed class DevicesModel(SecureAccessService access) : PageModel
{
    public SecureGrant? Grant { get; private set; }
    public IReadOnlyList<SecureRememberedBrowser> Browsers { get; private set; } = [];

    public string ReturnUrl => Grant?.Identity.Role switch
    {
        "DM" => "/dm/devices",
        "Player" => "/player",
        "Display" => "/display",
        _ => "/"
    };

    public async Task<IActionResult> OnGetAsync()
    {
        var actor = await Infrastructure.SecureAccess.Current(HttpContext);
        if (actor is null || actor.Identity.Role != "Player") return StatusCode(403);
        Grant = actor;
        Browsers = access.Remembered(actor);
        return Page();
    }

    public async Task<IActionResult> OnPostRememberAsync(string? label)
    {
        try
        {
            var actor = await Infrastructure.SecureAccess.Current(HttpContext)
                ?? throw new SecureAccessDenied();
            if (actor.Identity.Role != "Player") throw new SecureAccessDenied();
            access.Remember(actor, label);
            Infrastructure.SecureAccess.ExtendRememberedBrowserCookie(HttpContext);
            return RedirectToPage();
        }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
    }

    public async Task<IActionResult> OnPostForgetAsync(Guid id)
    {
        try
        {
            var actor = await Infrastructure.SecureAccess.Current(HttpContext)
                ?? throw new SecureAccessDenied();
            if (actor.Identity.Role != "Player") throw new SecureAccessDenied();
            var ownBrowser = access.Remembered(actor).Any(item =>
                item.Id == id && item.Browser == actor.Browser && item.Identity == actor.Identity);
            access.RevokeRemembered(actor, id);
            return ownBrowser ? LocalRedirect("/secure-access/wait") : RedirectToPage();
        }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
    }
}
