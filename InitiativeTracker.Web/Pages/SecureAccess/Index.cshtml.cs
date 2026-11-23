using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace InitiativeTracker.Web.Pages.SecureAccess;

public sealed record SecurityDeviceRow(Guid Id, string Owner, string Device, string Role,
    string BrowserReference, DateTimeOffset Expires, bool IsCurrent, bool Remembered);

[AllowAnonymous]
[RequestSizeLimit(8192)]
[RequestFormLimits(ValueLengthLimit = 256, ValueCountLimit = 16)]
public sealed class IndexModel(SecureAccessService access, ITrustedBrowserOrigin origin,
    IDbContextFactory<InitiativeTrackerDbContext> factory) : PageModel
{
    public bool IsPhysical { get; private set; }
    public bool IsDm { get; private set; }
    public IReadOnlyList<SecurityDeviceRow> Sessions { get; private set; } = [];
    public IReadOnlyList<SecurityDeviceRow> RememberedDevices { get; private set; } = [];
    public IReadOnlyList<SecureEnrollment> Requests { get; private set; } = [];

    private async Task<SecureGrant?> Authorized()
    {
        IsPhysical = origin.Physical(HttpContext);
        var grant = await Infrastructure.SecureAccess.Current(HttpContext);
        IsDm = grant?.Identity.Role == "DM";
        if (!IsPhysical && !IsDm) throw new SecureAccessDenied();
        return grant;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            var actor = await Authorized();
            var grants = access.Grants();
            var trusted = IsPhysical ? access.PhysicalRemembered() : access.Remembered(actor!);
            var visible = trusted.Where(x => !x.Revoked).ToArray();

            using var db = factory.CreateDbContext();
            var profiles = db.PlayerProfiles.AsNoTracking().ToDictionary(p => p.Id);
            var playerDevices = db.PlayerDeviceCredentials.AsNoTracking().ToDictionary(c => c.Id);
            var displays = db.RegisteredDevices.AsNoTracking().ToDictionary(d => d.Id);
            var browser = Infrastructure.SecureAccess.Browser(HttpContext);
            string Owner(SecureIdentity identity) => identity.Role switch
            {
                "DM" => "Dungeon Master",
                "Player" when identity.Profile is Guid id && profiles.TryGetValue(id, out var profile)
                    => profile.Name,
                "Display" when identity.Display is Guid id && displays.TryGetValue(id, out var device)
                    => device.Name,
                "Player" => "Player (profile unavailable)",
                _ => "Display (device unavailable)"
            };
            string Device(SecureIdentity identity, string binding)
            {
                var remembered = visible.FirstOrDefault(b => b.Browser == binding && b.Identity == identity);
                if (remembered is not null) return remembered.Label;
                if (identity.Role == "Player" && identity.Credential is Guid cid &&
                    playerDevices.TryGetValue(cid, out var credential)) return credential.Name;
                if (identity.Role == "Display" && identity.Display is Guid did &&
                    displays.TryGetValue(did, out var display)) return display.Name;
                return identity.Role == "DM" ? "DM browser" : "Web browser";
            }
            // Do not expose raw cookie values, credential hashes or certificates in the UI.
            Sessions = grants.OrderBy(g => Owner(g.Identity)).ThenBy(g => Device(g.Identity, g.Browser))
                .Select(g => new SecurityDeviceRow(g.Id, Owner(g.Identity), Device(g.Identity, g.Browser),
                    g.Identity.Role, g.Browser[..8], g.Expires,
                    g.Browser == browser && actor?.Id == g.Id,
                    visible.Any(t => t.Browser == g.Browser && t.Identity == g.Identity))).ToArray();
            RememberedDevices = visible.OrderBy(t => Owner(t.Identity)).ThenBy(t => t.Label)
                .Select(t => new SecurityDeviceRow(t.Id, Owner(t.Identity), Device(t.Identity, t.Browser),
                    t.Identity.Role, t.Browser[..8], t.Expires,
                    t.Browser == browser && actor?.Identity == t.Identity, true)).ToArray();
            if (IsDm) Requests = access.MemberRequests();
            Response.Headers.CacheControl = "no-store, private";
            return Page();
        }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid id)
    {
        try
        {
            var actor = await Authorized();
            var selfRevoked = actor?.Id == id;
            if (IsPhysical) access.RevokePhysicalSession(id);
            else access.Revoke(actor!, id);
            if (selfRevoked)
            {
                await Infrastructure.SecureAccess.SignOutActive(HttpContext);
                return LocalRedirect(actor!.Identity.Role switch
                {
                    "DM" => "/dm/login",
                    "Player" => "/join",
                    "Display" => "/display/pair",
                    _ => "/"
                });
            }
            return RedirectToPage();
        }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
    }

    public async Task<IActionResult> OnPostForgetAsync(Guid id)
    {
        try
        {
            var actor = await Authorized();
            var selfForgotten = actor is not null &&
                (IsPhysical ? access.PhysicalRemembered() : access.Remembered(actor))
                    .Any(item => item.Id == id && item.Browser == actor.Browser && item.Identity == actor.Identity);
            if (IsPhysical) access.RevokePhysicalRemembered(id);
            else access.RevokeRemembered(actor!, id);
            if (selfForgotten)
            {
                await Infrastructure.SecureAccess.SignOutActive(HttpContext);
                return LocalRedirect(actor!.Identity.Role switch
                {
                    "DM" => "/dm/login",
                    "Player" => "/join",
                    "Display" => "/display/pair",
                    _ => "/"
                });
            }
            return RedirectToPage();
        }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
    }

    public async Task<IActionResult> OnPostApproveAsync(Guid id)
    {
        try
        {
            var actor = await Authorized();
            if (!IsDm || actor is null) throw new SecureAccessDenied();
            access.Approve(actor, id);
            return RedirectToPage();
        }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
    }
}
