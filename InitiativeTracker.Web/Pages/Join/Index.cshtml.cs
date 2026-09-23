using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.Join;

[AllowAnonymous]
public class IndexModel : PageModel
{
    private readonly GameSessionService
        _gameSession;

    private readonly PlayerRegistryService
        _playerRegistry;

    public IndexModel(
        GameSessionService gameSession,
        PlayerRegistryService playerRegistry)
    {
        _gameSession =
            gameSession;

        _playerRegistry =
            playerRegistry;
    }

    [BindProperty]
    public string PlayerName { get; set; } =
        string.Empty;

    [BindProperty]
    public string JoinCode { get; set; } =
        string.Empty;

    [BindProperty]
    public string JoinToken { get; set; } =
        string.Empty;

    public bool TokenAccepted { get; private set; }

    public void OnGet(
        string? t)
    {
        if (!string.IsNullOrWhiteSpace(t) &&
            _gameSession.ValidatePlayerToken(t))
        {
            JoinToken =
                t;

            TokenAccepted =
                true;
        }
        else if (!string.IsNullOrWhiteSpace(t))
        {
            ModelState.AddModelError(
                string.Empty,
                "This game invitation is invalid or has expired.");
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(
                PlayerName))
        {
            ModelState.AddModelError(
                nameof(PlayerName),
                "Your name is required.");

            return Page();
        }

        var joinedWithToken =
            !string.IsNullOrWhiteSpace(
                JoinToken) &&
            _gameSession.ValidatePlayerToken(
                JoinToken);

        var joinedWithCode =
            !string.IsNullOrWhiteSpace(
                JoinCode) &&
            _gameSession.ValidateJoinCode(
                JoinCode);

        if (!joinedWithToken &&
            !joinedWithCode)
        {
            ModelState.AddModelError(
                nameof(JoinCode),
                "Game code was not found.");

            return Page();
        }

        var profileToken =
            Request.Cookies[
                "InitiativeTracker.PlayerProfile"];

        var player =
            _playerRegistry.JoinPlayer(
                _gameSession
                    .CurrentSession
                    .SessionId,
                PlayerName,
                profileToken,
                out var profile);

        Response.Cookies.Append(
            "InitiativeTracker.PlayerProfile",
            profile.AccessToken,
            new CookieOptions
            {
                HttpOnly = true,

                SameSite =
                    SameSiteMode.Lax,

                Secure =
                    Request.IsHttps,

                Expires =
                    DateTimeOffset.UtcNow
                        .AddYears(1)
            });

        await DeviceAuthenticationService
            .SignInAsync(
                HttpContext,
                _gameSession.CurrentSession,
                "Player",
                lifetime:
                    TimeSpan.FromHours(12),
                playerId:
                    player.Id,
                playerProfileId:
                    profile.Id);

        return RedirectToPage(
            "/Player/Index");
    }
}