using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.Dm;

[Authorize(Roles = "DM")]
public class JoinModel : PageModel
{
    private readonly GameSessionService
        _gameSession;

    private readonly TrackerUrlService
        _trackerUrl;

    public JoinModel(
        GameSessionService gameSession,
        TrackerUrlService trackerUrl)
    {
        _gameSession =
            gameSession;

        _trackerUrl =
            trackerUrl;
    }

    public GameSessionRecord Session =>
        _gameSession.CurrentSession;

    public string HubAddress { get; private set; } =
        string.Empty;

    public string PlayerJoinUrl { get; private set; } =
        string.Empty;

    public void OnGet()
    {
        HubAddress =
            _trackerUrl
                .GetManualJoinUrl(
                    Request);

        PlayerJoinUrl =
            _trackerUrl
                .GetPlayerJoinUrl(
                    Request,
                    Session.PlayerJoinToken);
    }
}
