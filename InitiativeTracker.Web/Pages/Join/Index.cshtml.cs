using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Infrastructure;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.Join;

[AllowAnonymous]
public class IndexModel : PageModel
{
    private const string PlayerDeviceCookie =
        "InitiativeTracker.PlayerDevice";

    private const string LegacyProfileCookie =
        "InitiativeTracker.PlayerProfile";

    private readonly GameSessionService
        _gameSession;

    private readonly PlayerRegistryService _playerRegistry;
    private readonly ITrustedBrowserOrigin _secureOrigin;

    public IndexModel(GameSessionService gameSession, PlayerRegistryService playerRegistry,
        ITrustedBrowserOrigin secureOrigin)
    {
        _gameSession = gameSession;
        _playerRegistry = playerRegistry;
        _secureOrigin = secureOrigin;
    }

    // New profiles must see their one-time recovery key before requesting approval.
    public string ContinueUrl => _secureOrigin.TrustedRequest(HttpContext)
        ? "/secure-access/wait?start=true" : "/player";

    [BindProperty]
    public string PlayerName { get; set; } =
        string.Empty;

    [BindProperty]
    public string JoinCode { get; set; } =
        string.Empty;

    [BindProperty]
    public string JoinToken { get; set; } =
        string.Empty;

    [BindProperty]
    public string RecoveryKey { get; set; } =
        string.Empty;

    [BindProperty]
    public string JoinMode { get; set; } =
        string.Empty;

    public bool TokenAccepted { get; private set; }

    public bool HasRememberedProfile =>
        RememberedProfile is not null;

    public PlayerProfile? RememberedProfile { get; private set; }

    public bool JoinCompleted { get; private set; }

    public string? RecoveryKeyToShow { get; private set; }


    public void OnGet(
        string? t,
        bool joined = false)
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


        LoadRememberedProfile();


        if (joined &&
            User.IsInRole("Player"))
        {
            JoinCompleted =
                true;

            RecoveryKeyToShow =
                TempData[
                    "NewRecoveryKey"]
                as string;
        }
    }


    public async Task<IActionResult> OnPostAsync()
    {
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

            LoadRememberedProfile();

            return Page();
        }


        PlayerJoinResult result;

        try
        {
            result =
                JoinMode switch
                {
                    "continue" =>
                        _playerRegistry
                            .JoinRememberedPlayer(
                                _gameSession
                                    .CurrentSession
                                    .SessionId,
                                Request.Cookies[
                                    PlayerDeviceCookie],
                                Request.Cookies[
                                    LegacyProfileCookie]),

                    "recover" =>
                        _playerRegistry
                            .RecoverPlayer(
                                _gameSession
                                    .CurrentSession
                                    .SessionId,
                                RecoveryKey),

                    _ =>
                        _playerRegistry
                            .CreatePlayer(
                                _gameSession
                                    .CurrentSession
                                    .SessionId,
                                PlayerName)
                };
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or InvalidOperationException)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            LoadRememberedProfile();

            return Page();
        }


        if (!string.IsNullOrWhiteSpace(
                result.DeviceTokenToStore))
        {
            Response.Cookies.Append(
                PlayerDeviceCookie,
                result.DeviceTokenToStore,
                new CookieOptions
                {
                    HttpOnly =
                        true,

                    SameSite =
                        SameSiteMode.Lax,

                    Secure =
                        Request.IsHttps,

                    Expires =
                        DateTimeOffset.UtcNow
                            .AddYears(2),

                    IsEssential =
                        true
                });
        }


        /*
         * Once this browser has a per-device credential,
         * the old profile-wide cookie is no longer needed.
         */
        Response.Cookies.Delete(
            LegacyProfileCookie);


        await DeviceAuthenticationService
            .SignInAsync(
                HttpContext,
                _gameSession.CurrentSession,
                "Player",
                lifetime:
                    TimeSpan.FromHours(12),
                playerId:
                    result.Connection.Id,
                playerProfileId:
                    result.Profile.Id,
                playerDeviceCredentialId:
                    result.DeviceCredential.Id);


        if (!string.IsNullOrWhiteSpace(
                result.RecoveryKeyToShow))
        {
            TempData[
                "NewRecoveryKey"] =
                result.RecoveryKeyToShow;

            return RedirectToPage(
                new
                {
                    joined = true
                });
        }


        return _secureOrigin.TrustedRequest(HttpContext)
            ? RedirectToPage("/SecureAccess/Wait", new { start = true })
            : RedirectToPage("/Player/Index");
    }


    private void LoadRememberedProfile()
    {
        RememberedProfile =
            _playerRegistry
                .FindRememberedProfile(
                    Request.Cookies[
                        PlayerDeviceCookie],
                    Request.Cookies[
                        LegacyProfileCookie]);
    }
}
