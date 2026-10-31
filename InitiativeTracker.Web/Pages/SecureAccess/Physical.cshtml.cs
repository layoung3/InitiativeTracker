using InitiativeTracker.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc;

namespace InitiativeTracker.Web.Pages.SecureAccess;

[AllowAnonymous]
public sealed class PhysicalModel(SecureAccessService access) : PageModel
{
    public IReadOnlyList<SecureEnrollment> Requests { get; private set; } = [];
    public IActionResult OnPostClearRequests() { access.ClearPending(); return RedirectToPage(); }
    public void OnGet() { Requests = access.PhysicalRequests(); Response.Headers["Refresh"] = "5"; }
}
