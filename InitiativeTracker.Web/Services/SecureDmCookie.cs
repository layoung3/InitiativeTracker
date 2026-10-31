using Microsoft.AspNetCore.Authentication.Cookies;

namespace InitiativeTracker.Web.Services;

public static class SecureDmCookie
{
    public static void Configure(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = DmAuthenticationPolicy.SecureCookie;
        options.Cookie.Path = "/";
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.LoginPath = "/dm/login";
        options.AccessDeniedPath = "/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = false;
        options.Events.OnValidatePrincipal = context =>
        {
            var services = context.HttpContext.RequestServices;
            var policy = services.GetRequiredService<DmAuthenticationPolicy>();
            var session = services.GetRequiredService<GameSessionService>().CurrentSession.SessionId;
            if (!policy.SecureDm(context.HttpContext, context.Principal) || context.Principal?.FindFirst("session_id")?.Value != session.ToString())
                context.RejectPrincipal();
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Method != "GET") context.Response.StatusCode = StatusCodes.Status403Forbidden;
            else context.Response.Redirect("/dm/login");
            return Task.CompletedTask;
        };
    }
}
