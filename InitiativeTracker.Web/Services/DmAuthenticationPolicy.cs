using System.Net;
using InitiativeTracker.Core.Coordination;

namespace InitiativeTracker.Web.Services;

/// <summary>Browser trust is independent of agent pins and legacy gameplay cookies.</summary>
public sealed class DmAuthenticationPolicy(IConfiguration configuration, IProvisioningHttps tls)
{
    public const string BrowserScheme = "BrowserSession";
    public const string SecureScheme = "SecureDm";
    public const string SecureCookie = "__Host-InitiativeTracker.SecureDm";
    public const string NetworkScheme = "NetworkDmSession";
    public const string NetworkPolicy = "NetworkAdministration";
    public const string LoginIdClaim = "dm_login_id";
    public bool CoordinationEnabled => configuration.GetValue<bool>("Coordination:Enabled");
    public bool RequiresTrustedPin => CoordinationEnabled || configuration.GetValue<bool>("DmAuthentication:RequireTrustedHttps");
    public int BrowserPort => configuration.GetValue<int?>("BrowserTls:Port") ?? 5445;
    public string? BrowserOrigin => tls.Ready && tls.Host is { } host
        ? new UriBuilder("https", host, BrowserPort).Uri.GetLeftPart(UriPartial.Authority) : null;

    public bool TrustedHttps(HttpContext context)
    {
        if (!context.Request.IsHttps || !tls.Ready || context.Connection.LocalPort != BrowserPort ||
            (context.Request.Host.Port ?? 443) != BrowserPort) return false;
        if (string.Equals(context.Request.Host.Host, tls.Host, StringComparison.OrdinalIgnoreCase)) return true;
        // Optional physical localhost TLS needs an actual matching SAN, never a hostname bypass.
        return CoordinationEndpoints.Local(context) && tls.Certificate is { } certificate &&
            certificate.MatchesHostname(context.Request.Host.Host.Trim('[', ']'), allowWildcards: false, allowCommonName: false);
    }

    public bool PhysicalScreen(HttpContext context) => CoordinationEndpoints.Local(context) ||
        TrustedHttps(context) && context.Connection.RemoteIpAddress is { } remote && IPAddress.IsLoopback(remote);
    public bool PinSubmissionAllowed(HttpContext context) => TrustedHttps(context) || !RequiresTrustedPin && !context.Request.IsHttps;
    public bool SecureDm(HttpContext context, System.Security.Claims.ClaimsPrincipal? principal = null)
    {
        principal ??= context.User;
        return TrustedHttps(context) && principal.Identity?.AuthenticationType == SecureScheme && principal.IsInRole("DM") &&
            Guid.TryParse(principal.FindFirst(LoginIdClaim)?.Value, out _) && Guid.TryParse(principal.FindFirst("session_id")?.Value, out _);
    }
}
