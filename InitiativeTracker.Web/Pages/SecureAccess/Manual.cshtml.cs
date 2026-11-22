using InitiativeTracker.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.SecureAccess;

[AllowAnonymous]
[RequestSizeLimit(8192)]
[RequestFormLimits(ValueLengthLimit = 256, ValueCountLimit = 16)]
public sealed class ManualModel(SecureAccessService access, ITrustedBrowserOrigin origin) : PageModel
{
    public SecureEnrollment? Pending { get; private set; }
    public SecureGrant? Grant { get; private set; }
    public IReadOnlyList<SecureEnrollment> Requests { get; private set; } = [];
    public IReadOnlyList<SecureGrant> Grants { get; private set; } = [];
    public IReadOnlyList<SecureRememberedBrowser> TrustedBrowsers { get; private set; } = [];
    public bool Initialized => access.Initialized;
    // A QR quick-connect Display identity has no trusted device-id yet; do not
    // offer a secure enrollment action that will always fail with 403.
    public bool CanEnrollMember => access.Capture(User)?.Role is "Player" or "Display";
    private string Binding => Infrastructure.SecureAccess.Browser(HttpContext) ?? throw new SecureAccessDenied();
    private string Peer => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    public async Task<IActionResult> OnGetAsync()
    {
        var browser = Infrastructure.SecureAccess.Browser(HttpContext, create: true)!;
        Pending = access.ForBrowser(browser);
        Grant = await Infrastructure.SecureAccess.Current(HttpContext);
        if (Grant is null && Pending is null)
        {
            var restored = access.ResumeRemembered(browser, origin.Origin, User);
            if (restored is not null)
            {
                await Infrastructure.SecureAccess.SignIn(HttpContext, restored);
                return RedirectToPage();
            }
        }
        if (Grant is not null)
        {
            TrustedBrowsers = access.Remembered(Grant);
            if (Grant.Identity.Role == "DM") { Requests = access.MemberRequests(); Grants = access.Grants(); }
        }
        return Page();
    }
    public IActionResult OnPostBeginDm(bool recovery = false) => Attempt(() =>
        access.Begin(recovery ? "dm-recover" : "dm-bootstrap", Binding, origin.Origin, Peer, User, physical: origin.Physical(HttpContext)));
    public IActionResult OnPostBeginMember() => Attempt(() =>
    {
        var identity = access.Capture(User) ?? throw new SecureAccessDenied();
        access.Begin(identity.Role == "Player" ? "enroll-player" : "enroll-display", Binding, origin.Origin, Peer, User);
    });
    public async Task<IActionResult> OnPostRedeemAsync()
    {
        try
        {
            var grant = access.RedeemDm(Binding, origin.Origin, (await Request.ReadFormAsync())["Code"].ToString(), Peer);
            await Infrastructure.SecureAccess.SignIn(HttpContext, grant);
            return RedirectToPage();
        }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
        finally { ModelState.Clear(); }
    }
    public async Task<IActionResult> OnPostFinishPhysicalAsync()
    {
        try
        {
            var grant = access.FinishPhysicalDm(Binding, origin.Origin);
            await Infrastructure.SecureAccess.SignIn(HttpContext, grant);
            return RedirectToPage();
        }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
    }
    public async Task<IActionResult> OnPostApproveAsync(Guid id)
    {
        try { access.Approve(await Infrastructure.SecureAccess.Current(HttpContext) ?? throw new SecureAccessDenied(), id); return RedirectToPage(); }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
    }
    public async Task<IActionResult> OnPostFinishAsync()
    {
        try
        {
            var grant = access.Finish(Binding, origin.Origin, User);
            await Infrastructure.SecureAccess.SignIn(HttpContext, grant);
            return RedirectToPage();
        }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
    }
    public async Task<IActionResult> OnPostRememberAsync(string? label)
    {
        try
        {
            var actor = await Infrastructure.SecureAccess.Current(HttpContext) ?? throw new SecureAccessDenied();
            access.Remember(actor, label);
            Infrastructure.SecureAccess.ExtendRememberedBrowserCookie(HttpContext);
            return RedirectToPage();
        }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
    }
    public async Task<IActionResult> OnPostForgetRememberedAsync(Guid id)
    {
        try
        {
            var actor = await Infrastructure.SecureAccess.Current(HttpContext) ?? throw new SecureAccessDenied();
            access.RevokeRemembered(actor, id);
            return RedirectToPage();
        }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
    }
    public IActionResult OnPostCancel() => Attempt(() => access.Cancel(Binding));
    public async Task<IActionResult> OnPostRevokeAsync(Guid id)
    {
        try { access.Revoke(await Infrastructure.SecureAccess.Current(HttpContext) ?? throw new SecureAccessDenied(), id); return RedirectToPage(); }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
    }
    private IActionResult Attempt(Action action)
    {
        try { action(); return RedirectToPage(); }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
    }
}
