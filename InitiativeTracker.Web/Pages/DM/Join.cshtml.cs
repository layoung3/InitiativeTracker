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

    public JoinModel(
        GameSessionService gameSession)
    {
        _gameSession =
            gameSession;
    }

    public GameSessionRecord Session =>
        _gameSession.CurrentSession;

    public void OnGet()
    {
    }
}