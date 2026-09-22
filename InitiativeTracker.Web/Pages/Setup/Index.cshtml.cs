using System.Net;
using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.Setup;

public class IndexModel : PageModel
{
    private readonly GameSessionService
        _gameSession;

    public IndexModel(
        GameSessionService gameSession)
    {
        _gameSession = gameSession;
    }

    public GameSessionRecord Session =>
        _gameSession.CurrentSession;

    public IActionResult OnGet()
    {
        var remoteIp =
            HttpContext.Connection
                .RemoteIpAddress;

        if (remoteIp is null ||
            !IPAddress.IsLoopback(remoteIp))
        {
            return NotFound();
        }

        return Page();
    }

    public string DisplayPairingUrl =>
        $"{Request.Scheme}://{Request.Host}" +
        $"/display/connect?token=" +
        Uri.EscapeDataString(
            Session.DisplayToken);
}