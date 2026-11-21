using System.Net;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.Setup;

// Matches the physical Setup page's loopback boundary. This endpoint has no
// POST handlers and never exposes saved network credentials or Wi-Fi commands.
public sealed class NetworkStatusModel(NetworkStatusService networkStatus) : PageModel
{
    public NetworkStatusService.Snapshot Status { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (HttpContext.Connection.RemoteIpAddress is not IPAddress peer ||
            !IPAddress.IsLoopback(peer)) return NotFound();

        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Status = await networkStatus.GetAsync(cancellationToken);
        return Page();
    }
}
