using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Infrastructure;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.Display;

[AllowAnonymous]
public class PairModel : PageModel
{
    private const string PairingCookie =
        "InitiativeTracker.PairingRequest";

    private static readonly TimeSpan
        DisplayAuthenticationLifetime =
            TimeSpan.FromDays(
                365 * 5);

    private readonly DeviceRegistryService
        _deviceRegistry;

    private readonly GameSessionService _gameSession;
    private readonly ITrustedBrowserOrigin _secureOrigin;

    public PairModel(DeviceRegistryService deviceRegistry, GameSessionService gameSession,
        ITrustedBrowserOrigin secureOrigin)
    {
        _deviceRegistry = deviceRegistry;
        _gameSession = gameSession;
        _secureOrigin = secureOrigin;
    }

    public DevicePairingRequest PairingRequest
    {
        get;
        private set;
    } = null!;

    public IActionResult OnGet()
    {
        if (User.IsInRole("Display"))
        {
            return RedirectToPage(
                "/Display/Index");
        }

        PairingRequest =
            GetOrCreateRequest();

        return Page();
    }

    public async Task<IActionResult>
        OnGetStatusAsync()
    {
        if (!TryGetRequestId(
                out var requestId))
        {
            return new JsonResult(new
            {
                status = "missing"
            });
        }

        var request =
            _deviceRegistry
                .GetPairingRequest(
                    requestId);

        if (request is null)
        {
            return new JsonResult(new
            {
                status = "missing"
            });
        }

        if (request.ExpiresAtUtc <=
            DateTime.UtcNow)
        {
            return new JsonResult(new
            {
                status = "expired"
            });
        }

        if (request.Status ==
            "Rejected")
        {
            return new JsonResult(new
            {
                status = "rejected"
            });
        }

        if (request.Status ==
                "Approved" &&
            request.ApprovedDeviceId.HasValue)
        {
            await DeviceAuthenticationService
                .SignInAsync(
                    HttpContext,
                    _gameSession.CurrentSession,
                    "Display",
                    request.ApprovedDeviceId,
                    DisplayAuthenticationLifetime);

            _deviceRegistry
                .MarkPairingClaimed(
                    request.Id);

            Response.Cookies.Delete(
                PairingCookie);

            return new JsonResult(new
            {
                status = "approved",
                redirect = _secureOrigin.TrustedRequest(HttpContext)
                    ? "/secure-access/wait?start=true" : "/display"
            });
        }

        return new JsonResult(new
        {
            status = "pending"
        });
    }

    public IActionResult OnPostRestart()
    {
        Response.Cookies.Delete(
            PairingCookie);

        return RedirectToPage();
    }

    private DevicePairingRequest
        GetOrCreateRequest()
    {
        if (TryGetRequestId(
                out var requestId))
        {
            var existing =
                _deviceRegistry
                    .GetPairingRequest(
                        requestId);

            if (existing is not null &&
                existing.ExpiresAtUtc >
                    DateTime.UtcNow &&
                existing.Status !=
                    "Rejected")
            {
                return existing;
            }
        }

        var request =
            _deviceRegistry
                .CreatePairingRequest();

        Response.Cookies.Append(
            PairingCookie,
            request.Id.ToString(),
            new CookieOptions
            {
                HttpOnly = true,
                SameSite =
                    SameSiteMode.Strict,

                Secure =
                    Request.IsHttps,

                Expires =
                    DateTimeOffset.UtcNow
                        .AddMinutes(10)
            });

        return request;
    }

    private bool TryGetRequestId(
        out Guid requestId)
    {
        var value =
            Request.Cookies[
                PairingCookie];

        return Guid.TryParse(
            value,
            out requestId);
    }
}