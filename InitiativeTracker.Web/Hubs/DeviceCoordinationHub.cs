using InitiativeTracker.Core.Coordination;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.SignalR;

namespace InitiativeTracker.Web.Hubs;

// No cookie role grants entry. TLS client certificate is rechecked on every invocation.
public sealed class DeviceCoordinationHub(DeviceCoordinationService coordination) : Hub
{
    private T WithCertificate<T>(Func<System.Security.Cryptography.X509Certificates.X509Certificate2,T> action)
    {
        var raw = Context.User?.FindFirst("device_certificate")?.Value;
        if (raw is null) throw new HubException("Device TLS authentication required");
        using var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(Convert.FromBase64String(raw));
        return action(certificate);
    }
    public override Task OnConnectedAsync()
    {
        WithCertificate(coordination.Authenticate); return base.OnConnectedAsync();
    }
    public DeviceStatus Poll() => WithCertificate(coordination.Status);
    public void Receipt(DeviceReceipt receipt) => WithCertificate(certificate =>
    { coordination.Receipt(certificate, receipt, DateTimeOffset.UtcNow); return true; });
}

public sealed class DeviceCoordinationWorker(DeviceCoordinationService service, IHubContext<DeviceCoordinationHub> hub,
    ILogger<DeviceCoordinationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!service.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await service.TickAsync(stoppingToken);
                await hub.Clients.All.SendAsync("PlanAvailable", cancellationToken: stoppingToken);
            }
            catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or InvalidOperationException or OperationCanceledException)
            { logger.LogWarning("Device coordination tick failed ({FailureType}); independent root recovery remains armed", ex.GetType().Name); }
        }
    }
}
