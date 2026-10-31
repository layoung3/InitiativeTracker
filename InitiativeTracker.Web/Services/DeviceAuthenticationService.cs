using InitiativeTracker.Web.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

namespace InitiativeTracker.Web.Services;

public static class DeviceAuthenticationService
{
    public static async Task SignInAsync(
        HttpContext httpContext,
        GameSessionRecord session,
        string role,
        Guid? deviceId = null,
        TimeSpan? lifetime = null,
        Guid? playerId = null,
        Guid? playerProfileId = null,
        Guid? playerDeviceCredentialId = null)
    {
        var claims =
            new List<Claim>
            {
            new(
                ClaimTypes.Role,
                role),

            new(
                "session_id",
                session.SessionId.ToString())
            };

        if (deviceId.HasValue)
        {
            claims.Add(
                new Claim(
                    "device_id",
                    deviceId.Value.ToString()));
        }
        if (playerId.HasValue)
        {
            claims.Add(
                new Claim(
                    "player_id",
                    playerId.Value.ToString()));
        }
        if (playerProfileId.HasValue)
        {
            claims.Add(
                new Claim(
                    "player_profile_id",
                    playerProfileId.Value.ToString()));
        }

        if (playerDeviceCredentialId.HasValue)
        {
            claims.Add(
                new Claim(
                    "player_device_id",
                    playerDeviceCredentialId.Value.ToString()));
        }

        var scheme = CookieAuthenticationDefaults.AuthenticationScheme;
        if (role == "DM")
        {
            var policy = httpContext.RequestServices.GetRequiredService<DmAuthenticationPolicy>();
            if (!policy.PinSubmissionAllowed(httpContext)) throw new InvalidOperationException("Trusted browser HTTPS is required for DM authentication.");
            if (policy.TrustedHttps(httpContext))
            {
                scheme = DmAuthenticationPolicy.SecureScheme;
                claims.Add(new Claim(DmAuthenticationPolicy.LoginIdClaim, Guid.NewGuid().ToString()));
            }
        }
        var identity =
            new ClaimsIdentity(
                claims,
                scheme);

        var principal =
            new ClaimsPrincipal(identity);

        var properties =
            new AuthenticationProperties
            {
                IsPersistent = true
            };

        if (lifetime.HasValue)
        {
            properties.ExpiresUtc =
                DateTimeOffset.UtcNow
                    .Add(lifetime.Value);
        }

        await httpContext.SignInAsync(
            scheme,
            principal,
            properties);
    }
}
