using InitiativeTracker.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace InitiativeTracker.Web.Pages.SecureAccess;

[AllowAnonymous]
public sealed class PhysicalModel(SecureAccessService access) : PageModel
{
    public IReadOnlyList<SecureEnrollment> Requests { get; private set; } = [];
    public IActionResult OnPostApprove(Guid id)
    {
        try { access.ApprovePhysicalDm(id); return RedirectToPage(); }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
    }
    public IActionResult OnPostDeny(Guid id)
    {
        try { access.DenyPhysicalDm(id); return RedirectToPage(); }
        catch (SecureAccessDenied) { return StatusCode(403); }
        catch (SecureAccessUnavailable) { return StatusCode(503); }
    }
    public IActionResult OnPostClearRequests() { access.ClearPending(); return RedirectToPage(); }
    public void OnGet() { Requests = access.PhysicalRequests(); Response.Headers["Refresh"] = "5"; }
}
