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

    private readonly HubIdentityService
        _hubIdentity;

    public JoinModel(
        GameSessionService gameSession,
        TrackerUrlService trackerUrl,
        HubIdentityService hubIdentity)
    {
        _gameSession =
            gameSession;

        _trackerUrl =
            trackerUrl;

        _hubIdentity =
            hubIdentity;
    }

    public GameSessionRecord Session =>
        _gameSession.CurrentSession;

    public string HubAddress { get; private set; } =
        string.Empty;

    public string PlayerJoinUrl { get; private set; } =
        string.Empty;

    public string HubHostName =>
        _hubIdentity.HostName;

    public string HubMdnsName =>
        _hubIdentity.MdnsHostName;

    public Guid HubId =>
        _hubIdentity.HubId;

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
