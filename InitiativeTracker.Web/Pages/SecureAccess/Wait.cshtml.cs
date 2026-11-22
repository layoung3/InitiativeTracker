using InitiativeTracker.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.SecureAccess;

// This is the normal UX after an existing, validated game login. A GET never
// starts an enrollment or issues a new authorization; the browser uses a
// Razor-generated antiforgery form and same-origin POST to request approval.
[AllowAnonymous]
[RequestSizeLimit(8192)]
[RequestFormLimits(ValueLengthLimit = 256, ValueCountLimit = 16)]
public sealed class WaitModel(SecureAccessService access, ITrustedBrowserOrigin origin) : PageModel
{
    public SecureEnrollment? Pending { get; private set; }
    public bool AutoBegin { get; private set; }
    public bool HasIdentity { get; private set; }
    public string Role { get; private set; } = "";

    private static string Landing(string role) => role switch
    {
        "DM" => "/dm",
        "Player" => "/player",
        "Display" => "/display",
        _ => "/"
    };

    public async Task<IActionResult> OnGetAsync(bool start = false)
    {
        // The global SecureAccess guard has already verified trusted HTTPS.
        var current = await Infrastructure.SecureAccess.Current(HttpContext);
        if (current is not null) return LocalRedirect(Landing(current.Identity.Role));

        var browser = Infrastructure.SecureAccess.Browser(HttpContext, create: true);
        var legacy = access.Capture(User);
        HasIdentity = legacy is not null;
        Role = legacy?.Role ?? "";

        if (browser is not null && legacy is not null)
        {
            var request = access.ForBrowser(browser);
            Pending = request is not null && request.Identity == legacy with { Stamp = "" }
                ? request : null;
            if (Pending is null)
            {
                var restored = access.ResumeRemembered(browser, origin.Origin, User);
                if (restored is not null)
                {
                    await Infrastructure.SecureAccess.SignIn(HttpContext, restored);
                    return LocalRedirect(Landing(restored.Identity.Role));
                }
            }
        }

        // A denied/expired request is never automatically reissued by polling.
        // Only the login/join redirect may set start=true; Retry is an explicit POST.
        AutoBegin = start && HasIdentity && Pending is null;
        return Page();
    }

    public async Task<IActionResult> OnPostBeginAsync()
    {
        try
        {
            var legacy = access.Capture(User) ?? throw new SecureAccessDenied();
            if (await Infrastructure.SecureAccess.Current(HttpContext) is { } current)
                return LocalRedirect(Landing(current.Identity.Role));
            var browser = Infrastructure.SecureAccess.Browser(HttpContext, create: true)
                ?? throw new SecureAccessDenied();
            if (access.ForBrowser(browser) is { } pending &&
                pending.Identity == legacy with { Stamp = "" })
                return RedirectToPage();

            var purpose = legacy.Role switch
            {
                "DM" => access.Initialized ? "dm-bootstrap" : "dm-recover",
                "Player" => "enroll-player",
                "Display" => "enroll-display",
                _ => throw new SecureAccessDenied()
            };
            access.Begin(purpose, browser, origin.Origin,
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                User, physical: legacy.Role == "DM" && origin.Physical(HttpContext));
            return RedirectToPage();
        }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
    }

    public IActionResult OnPostRememberPreference(Guid id, bool remember = false)
    {
        try
        {
            var identity = access.Capture(User) ?? throw new SecureAccessDenied();
            var browser = Infrastructure.SecureAccess.Browser(HttpContext)
                ?? throw new SecureAccessDenied();
            access.SetRememberPreference(browser, id, identity, remember);
            return RedirectToPage();
        }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
    }

    public async Task<IActionResult> OnPostFinishAsync()
    {
        try
        {
            var browser = Infrastructure.SecureAccess.Browser(HttpContext)
                ?? throw new SecureAccessDenied();
            var request = access.ForBrowser(browser) ?? throw new SecureAccessDenied();
            var grant = request.Purpose.StartsWith("dm-", StringComparison.Ordinal)
                ? access.FinishPhysicalDm(browser, origin.Origin)
                : access.Finish(browser, origin.Origin, User);
            await Infrastructure.SecureAccess.SignIn(HttpContext, grant);
            // The checkbox is explicitly saved on the exact pending request.
            // Neither a browser GET nor a physical hub approval implies consent.
            if (request.RememberRequested)
            {
                access.Remember(grant, grant.Identity.Role switch
                {
                    "DM" => "DM browser",
                    "Player" => "Player browser",
                    "Display" => "Display browser",
                    _ => throw new SecureAccessDenied()
                });
                Infrastructure.SecureAccess.ExtendRememberedBrowserCookie(HttpContext);
            }
            return LocalRedirect(Landing(grant.Identity.Role));
        }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
    }
}
