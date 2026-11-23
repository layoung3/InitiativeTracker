using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InitiativeTracker.Web.Infrastructure;

public interface ITrustedBrowserOrigin
{
    bool Enabled { get; }
    string Origin { get; }
    bool TrustedRequest(HttpContext context);
    bool Physical(HttpContext context);
}

public static class SecureAccess
{
    public const string Scheme = "SecureFeature";
    public const string Cookie = "__Host-InitiativeTracker.SecureAccess";
    public const string BrowserCookie = "__Host-InitiativeTracker.SecureBrowser";
    // FreshLoginClaim is set only by verified primary sign-in. Elevated
    // ordinary cookies instead carry GrantedLoginClaim and cannot reuse
    // their own remembered trust after active-session revocation.
    public const string FreshLoginClaim = "tracker_fresh_primary_login";
    public const string GrantedLoginClaim = "tracker_elevated_grant";
    private static readonly string[] IdentityClaims = [ClaimTypes.Role, "session_id", "player_profile_id", "player_id", "player_device_id", "device_id"];

    public static void Configure(WebApplicationBuilder builder, string dataDirectory)
    {
        var persistent = Path.IsPathFullyQualified(builder.Configuration["Tracker:DataDirectory"] ?? "") &&
            Path.GetFullPath(dataDirectory) != Path.GetFullPath(builder.Environment.ContentRootPath) &&
            !Path.GetFullPath(dataDirectory).StartsWith(Path.GetFullPath(builder.Environment.ContentRootPath) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        builder.Services.AddSingleton(new SecureAccessStorage(Path.Combine(dataDirectory, "secure-access"), persistent));
        builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<SecureGrantPersistence>();
        builder.Services.AddSingleton<SecureAccessService>();
        builder.Services.AddAntiforgery(options =>
        {
            // Preserve legacy HTTP forms; every newly issued HTTPS form cookie is Secure.
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        builder.Services.AddAuthentication().AddCookie(Scheme, options =>
        {
            options.Cookie.Name = Cookie;
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
            options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = context =>
            {
                if (Ticket(context.HttpContext, context.Principal) is null) context.RejectPrincipal();
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
        });
    }
    public static string? Browser(HttpContext context, bool create = false)
    {
        if (context.Request.Cookies.TryGetValue(BrowserCookie, out var value) && value.Length == 64 && value.All(Uri.IsHexDigit)) return SecureAccessService.Hash(value);
        if (!create || !context.RequestServices.GetRequiredService<ITrustedBrowserOrigin>().TrustedRequest(context)) return null;
        value = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        context.Response.Cookies.Append(BrowserCookie, value, new CookieOptions
        { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", IsEssential = true, MaxAge = TimeSpan.FromDays(30) });
        return SecureAccessService.Hash(value);
    }
    // Upgrade the cookie from a previously installed 12-hour browser binding to
    // the approved 30-day lifetime only after an explicit persisted opt-in.
    // The raw token is read from the HTTPS request and sent only as a host-only
    // Secure/HttpOnly response cookie; no server state stores its plaintext.
    public static void ExtendRememberedBrowserCookie(HttpContext context)
    {
        var origin = context.RequestServices.GetRequiredService<ITrustedBrowserOrigin>();
        if (!origin.TrustedRequest(context) ||
            !context.Request.Cookies.TryGetValue(BrowserCookie, out var raw) ||
            raw.Length != 64 || !raw.All(Uri.IsHexDigit))
            throw new SecureAccessDenied();
        context.Response.Cookies.Append(BrowserCookie, raw, new CookieOptions
        {
            Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict,
            Path = "/", IsEssential = true, MaxAge = TimeSpan.FromDays(30)
        });
    }
    public static SecureGrant? Ticket(HttpContext context, ClaimsPrincipal? principal)
    {
        var origin = context.RequestServices.GetRequiredService<ITrustedBrowserOrigin>();
        if (!origin.TrustedRequest(context) || principal?.Identity?.AuthenticationType != Scheme ||
            !Guid.TryParse(principal.FindFirstValue("secure_grant"), out var id) || !Guid.TryParse(principal.FindFirstValue("secure_epoch"), out var epoch) ||
            !int.TryParse(principal.FindFirstValue("secure_generation"), out var generation) || Browser(context) is not { } browser) return null;
        var service = context.RequestServices.GetRequiredService<SecureAccessService>();
        var grant = service.Validate(id, generation, epoch, browser, origin.Origin);
        if (grant is null) return null;
        var expected = SecureAccessService.Principal(grant.Identity, Scheme);
        return IdentityClaims.All(type => principal.FindAll(type).Select(c => c.Value).SequenceEqual(expected.FindAll(type).Select(c => c.Value))) ? grant : null;
    }
    public static async Task<SecureGrant?> Current(HttpContext context)
    {
        var result = await context.AuthenticateAsync(Scheme);
        var grant = result.Succeeded ? Ticket(context, result.Principal) : null;
        return grant is not null && context.RequestServices.GetRequiredService<SecureAccessService>().MatchesLegacy(grant, context.User) ? grant : null;
    }
    public static async Task SignIn(HttpContext context, SecureGrant grant)
    {
        var secure = SecureAccessService.Principal(grant.Identity, Scheme);
        var identity = (ClaimsIdentity)secure.Identity!;
        identity.AddClaims([new("secure_grant", grant.Id.ToString()), new("secure_epoch", grant.Epoch.ToString()), new("secure_generation", grant.Generation.ToString())]);
        var properties = new AuthenticationProperties { IsPersistent = true, ExpiresUtc = grant.Expires, AllowRefresh = false };
        // Fresh physical/approved authority also supplies the ordinary principal needed by
        // existing gameplay handlers, without changing any other browser's credentials.
        var ordinary = SecureAccessService.Principal(grant.Identity, CookieAuthenticationDefaults.AuthenticationScheme);
        ((ClaimsIdentity)ordinary.Identity!).AddClaim(new(GrantedLoginClaim, grant.Id.ToString()));
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, ordinary, properties);
        await context.SignInAsync(Scheme, secure, properties);
    }
    // Do not remove the separate 30-day remembered-browser token.
    public static async Task SignOutActive(HttpContext context)
    {
        await context.SignOutAsync(Scheme);
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
    public static void Headers(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store, private";
        context.Response.Headers.Pragma = "no-cache";
        // no-referrer turns Chromium's legitimate form Origin into null.
        // same-origin preserves exact-origin CSRF checks without cross-origin leakage.
        context.Response.Headers["Referrer-Policy"] = "same-origin";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    }
    private static bool IsPage(HttpRequest request, string page, string path) =>
        string.Equals(request.RouteValues["page"]?.ToString(), page, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(request.Path.Value?.TrimEnd('/'), path, StringComparison.OrdinalIgnoreCase);
    private static bool PrivateRoute(HttpRequest request)
    {
        if (request.Path.StartsWithSegments("/media") || request.Path.StartsWithSegments("/appearance") ||
            IsPage(request, "/Player/EditCharacter", "/player/editcharacter")) return true;
        if (!HttpMethods.IsPost(request.Method)) return false;
        var handler = request.Query["handler"].ToString().ToUpperInvariant();
        if (request.Path.StartsWithSegments("/player/character"))
            return handler is not ("ENDTURN" or "ADJUSTHP" or "SETHP" or "SETTEMPORARYHP" or "TOGGLEINSPIRATION" or "SETEXHAUSTION" or
                "TOGGLECONDITION" or "ADDCUSTOMCONDITION" or "REMOVECONDITION" or "ADJUSTSPELLSLOT" or "RESTOREALLSPELLSLOTS" or
                "CASTSPELL" or "ENDCONCENTRATION" or "TOGGLESPELLPREPARED" or "TOGGLEINVENTORYITEMEQUIPPED" or "ADJUSTINVENTORYITEMQUANTITY");
        return IsPage(request, "/Player/Index", "/player") && handler is "CREATECHARACTER" or "ADDCHARACTER" or "REGENERATERECOVERYKEY" or "RENAMEPROFILE";
    }
    private static bool AdminMutation(HttpRequest request) => !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) &&
        (request.Path.StartsWithSegments("/dm/players") || request.Path.StartsWithSegments("/dm/profiles") || request.Path.StartsWithSegments("/dm/devices") ||
         IsPage(request, "/DM/Index", "/dm") && request.Query["handler"].ToString().ToUpperInvariant() is
            "ADDPLAYERCHARACTER" or "ASSIGNCONTROLLER" or "SETVISIBILITY" or "UNDO" or "RESTORE");
    private static bool IdentityMutation(HttpRequest request) => HttpMethods.IsPost(request.Method) &&
        (request.Path.StartsWithSegments("/dm/players") || request.Path.StartsWithSegments("/dm/profiles") || request.Path.StartsWithSegments("/dm/devices") ||
         IsPage(request, "/Player/Index", "/player") && request.Query["handler"].ToString().ToUpperInvariant() is
            "REGENERATERECOVERYKEY" or "REVOKEDEVICE" or "SIGNOUT" or "FORGETTHISDEVICE");
    public static bool NetworkAuthorityRoute(HttpRequest request) => request.Path.StartsWithSegments("/api/coordination") ||
        request.Path.StartsWithSegments("/hubs/device-coordination") || request.Path.StartsWithSegments("/dm/network") &&
        HttpMethods.IsPost(request.Method) && request.Query["handler"].ToString().ToUpperInvariant() is
            "PROVISION" or "VALIDATE" or "ROTATEHOTSPOTPASSWORD" or "UNLOCKNETWORK" or "APPROVEAGENT";

    public static async Task Guard(HttpContext context, RequestDelegate next)
    {
        var origin = context.RequestServices.GetRequiredService<ITrustedBrowserOrigin>();
        if (!origin.Enabled) { await next(context); return; }
        try
        {
            if (context.Request.Query["handler"].Count > 1) { context.Response.StatusCode = 400; return; }
            if (NetworkAuthorityRoute(context.Request)) { context.Response.StatusCode = 403; return; } // N8's independent authority is not integrated here.
            if (context.Request.Path.StartsWithSegments("/secure-access"))
            {
                Headers(context);
                if (!origin.TrustedRequest(context)) { context.Response.StatusCode = 426; return; }
                if (context.Request.Path.StartsWithSegments("/secure-access/physical") && !origin.Physical(context)) { context.Response.StatusCode = 403; return; }
                if (HttpMethods.IsPost(context.Request.Method) && context.Request.Headers.Origin.ToString() != origin.Origin)
                { context.Response.StatusCode = 403; return; }
            }
            if (context.Request.Path.StartsWithSegments("/dm/players") || context.Request.Path.StartsWithSegments("/dm/profiles") || context.Request.Path.StartsWithSegments("/dm/devices")) Headers(context);
            // A trusted-HTTPS DM page must never fall back to the ordinary
            // login cookie after its secure grant expires or is revoked.
            // Keep the separately gated legacy-HTTP compatibility surface
            // unchanged until an explicitly approved production cutover.
            if (origin.TrustedRequest(context) && context.User.IsInRole("DM") &&
                context.Request.Path.StartsWithSegments("/dm") &&
                !context.Request.Path.StartsWithSegments("/dm/login"))
            {
                Headers(context);
                var dmGrant = await Current(context);
                if (dmGrant?.Identity.Role != "DM")
                {
                    // A stale browser can explicitly retry approval. Merely
                    // navigating to /dm must not silently start a new request.
                    if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
                        context.Response.Redirect("/secure-access/wait");
                    else
                        context.Response.StatusCode = 403;
                    return;
                }
            }
            // A stale elevated player/display cookie must not retain page
            // or new SignalR handshake access after its secure grant ends.
            // Existing HTTP compatibility remains separately gated.
            if (origin.TrustedRequest(context) &&
                context.User.HasClaim(c => c.Type == GrantedLoginClaim) &&
                (context.Request.Path.StartsWithSegments("/player") ||
                 context.Request.Path.StartsWithSegments("/display") ||
                 context.Request.Path.StartsWithSegments("/hubs/encounter")) &&
                await Current(context) is null)
            {
                Headers(context);
                if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) &&
                    !context.Request.Path.StartsWithSegments("/hubs/encounter"))
                    context.Response.Redirect("/secure-access/wait");
                else
                    context.Response.StatusCode = 403;
                return;
            }
            var needsPrivate = PrivateRoute(context.Request);
            var needsAdmin = AdminMutation(context.Request);
            if (needsPrivate || needsAdmin)
            {
                Headers(context);
                if (!origin.TrustedRequest(context)) { context.Response.StatusCode = 426; return; }
                if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && context.Request.Headers.Origin.ToString() != origin.Origin)
                { context.Response.StatusCode = 403; return; }
                var grant = await Current(context);
                if (grant is null || needsAdmin && grant.Identity.Role != "DM" ||
                    needsPrivate && grant.Identity.Role == "Display" && (!HttpMethods.IsGet(context.Request.Method) || context.Request.Path.StartsWithSegments("/appearance") || context.Request.Path.StartsWithSegments("/player")))
                { context.Response.StatusCode = 403; return; }
                if (!context.RequestServices.GetRequiredService<SecureAccessService>().MediaOwnershipUnchanged(context))
                { context.Response.StatusCode = 404; return; }
                // Request-local validated snapshot; consumers must revalidate durable authority after awaits.
                context.Items[typeof(SecureGrant)] = grant;
            }
            if (IdentityMutation(context.Request) && context.User.Identity?.IsAuthenticated == true &&
                (IsPage(context.Request, "/Player/Index", "/player") ? context.User.IsInRole("Player") : context.User.IsInRole("DM")))
            {
                await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
                if (!context.Request.HasFormContentType) { context.Response.StatusCode = 400; return; }
                var form = await context.Request.ReadFormAsync();
                var ids = new List<Guid>();
                if (IsPage(context.Request, "/Player/Index", "/player"))
                {
                    // A player cannot revoke another profile by adding a bogus form field.
                    if (Guid.TryParse(context.User.FindFirstValue("player_profile_id"), out var profile)) ids.Add(profile);
                }
                else ids.AddRange(form.Concat(context.Request.Query).Where(pair => pair.Key.ToUpperInvariant() is "PROFILEID" or "ID" or "CHARACTERID" or "DEVICEID")
                    .SelectMany(pair => pair.Value).Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty));
                context.RequestServices.GetRequiredService<SecureAccessService>().InvalidateIdentities(ids);
            }
            await next(context);
        }
        catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; }
        catch (SecureAccessUnavailable) { context.Response.StatusCode = 503; }
        catch (SecureAccessDenied) { context.Response.StatusCode = 403; }
    }
}
