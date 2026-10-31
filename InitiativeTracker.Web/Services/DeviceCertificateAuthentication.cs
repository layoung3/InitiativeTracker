using System.Security.Claims;
using System.Text.Encodings.Web;
using InitiativeTracker.Core.Coordination;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace InitiativeTracker.Web.Services;

/// <summary>Authenticate at the real TLS request boundary; SignalR long-poll copies the principal, not TLS connection features.</summary>
public sealed class DeviceCertificateAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, DeviceCoordinationService service) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var certificate = Context.Connection.ClientCertificate;
        if (!Request.IsHttps || certificate is null) return Task.FromResult(AuthenticateResult.NoResult());
        try
        {
            var enrollment = service.Authenticate(certificate);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier,enrollment.DeviceId.ToString()),
                new Claim("device_certificate",Convert.ToBase64String(certificate.RawData))],Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal,Scheme.Name)));
        }
        catch (UnauthorizedAccessException) { return Task.FromResult(AuthenticateResult.Fail("Unenrolled device certificate")); }
    }
}
