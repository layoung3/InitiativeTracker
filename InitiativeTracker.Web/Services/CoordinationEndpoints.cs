using InitiativeTracker.Core.Coordination;
using InitiativeTracker.Web.Hubs;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using System.Security.Cryptography.X509Certificates;

namespace InitiativeTracker.Web.Services;

public static class CoordinationEndpoints
{
    public static X509Certificate2? Configure(WebApplicationBuilder builder, string directory)
    {
        if (!builder.Configuration.GetValue<bool>("Coordination:Enabled")) return null;
        var certificate = DeviceProtocol.Certificate(Path.Combine(directory, "coordination", "hub.pfx"), "InitiativeTracker Hub", true);
        var browserTls = new PublicCertificateHttps(builder.Configuration["BrowserTls:Host"], builder.Configuration["BrowserTls:Certificate"], builder.Configuration["BrowserTls:Password"]);
        builder.Services.AddSingleton<IProvisioningHttps>(browserTls);
        var port = builder.Configuration.GetValue<int?>("Coordination:Port") ?? 5443;
        builder.WebHost.ConfigureKestrel(options =>
        {
            if (!builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
                options.ListenAnyIP(builder.Configuration.GetValue<int?>("Tracker:LanPort") ?? 5000);
            if (browserTls.Ready) options.ListenAnyIP(builder.Configuration.GetValue<int?>("BrowserTls:Port") ?? 5445, listen => listen.UseHttps(browserTls.Certificate!));
            options.ListenAnyIP(port, listen => listen.UseHttps(tls =>
            {
                tls.ServerCertificate = certificate;
                tls.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;
                tls.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                // Self-signed device certificates are enrolled by exact pin. Unknown certificates can enroll only.
                tls.ClientCertificateValidation = (cert, _, _) => cert.NotAfter > DateTime.UtcNow && cert.NotBefore < DateTime.UtcNow;
            }));
        });
        return certificate;
    }
    public static void Map(WebApplication app)
    {
        app.MapHub<DeviceCoordinationHub>("/hubs/device-coordination").RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { AuthenticationSchemes = "DeviceCertificate" });
        app.MapPost("/api/coordination/enroll", (HttpContext context, EnrollmentRequest request, DeviceCoordinationService service) =>
        {
            if (!context.Request.IsHttps || context.Connection.ClientCertificate is null) return Results.Unauthorized();
            try { return Results.Ok(service.Request(context.Connection.ClientCertificate, request, DateTimeOffset.UtcNow)); }
            catch (InvalidOperationException) { return Results.BadRequest(); }
        }).RequireRateLimiting("device-enrollment");
        app.MapGet("/api/coordination/status", (HttpContext context, DeviceCoordinationService service) =>
        {
            if (!context.Request.IsHttps) return Results.Unauthorized();
            try { return Results.Ok(service.Status(context.Connection.ClientCertificate)); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
        });
        app.MapGet("/api/coordination/recovery", async (HttpContext context, DeviceCoordinationService service, CancellationToken ct) =>
        {
            if (!context.Request.IsHttps) return Results.Unauthorized();
            try
            {
                var device = service.Authenticate(context.Connection.ClientCertificate);
                var profiles = await service.RecoveryAsync(ct);
                return Results.Ok(new { generation = device.Generation, recovery = profiles.Recovery });
            }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
        });
    }
    public static string DataDirectory(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var value = configuration["Tracker:DataDirectory"];
        return string.IsNullOrWhiteSpace(value) ? Path.Combine(environment.ContentRootPath, "App_Data") :
            Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(environment.ContentRootPath, value));
    }
    public static bool Local(HttpContext context) => context.Connection.RemoteIpAddress is { } remote && System.Net.IPAddress.IsLoopback(remote) &&
        context.Request.Host.Host is "localhost" or "127.0.0.1" or "::1" or "[::1]";
}
