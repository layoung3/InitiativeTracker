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
        TimeSpan? lifetime = null)
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

        var identity =
            new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);

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
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            properties);
    }
}