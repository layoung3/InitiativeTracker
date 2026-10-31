using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using InitiativeTracker.Core.Coordination;
using InitiativeTracker.Satellite;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace InitiativeTracker.Tests.Fixtures;

// Actual short-lived Python broker decisions/journals. Only NetworkManager and the systemd
// activation adapter are simulated; these fixtures never install a service or modify host networking.
internal sealed class NativeSatelliteBroker : IAsyncDisposable
{
    public string DirectoryPath { get; }
    public string SocketPath => Path.Combine(DirectoryPath, "network.sock");
    private readonly Socket server = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    private readonly CancellationTokenSource stopping = new();
    private readonly Task serve;
    private readonly string repository;
    public bool DropNextRebindReply { get; set; }
    public bool RejectNextRebind { get; set; }
    public bool DropNextResetReply { get; set; }
    public bool FailAgentSaveAfterRebind { get; set; }
    public int RebindRequests { get; private set; }
    public int ResetRequests { get; private set; }
    public NativeSatelliteBroker(string directory)
    {
        DirectoryPath = directory;
        PrivateFile.Directory(directory);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "InitiativeTracker.slnx"))) root = root.Parent;
        repository = root?.FullName ?? throw new IOException("Fixture repository unavailable");
        server.Bind(new UnixDomainSocketEndPoint(SocketPath)); server.Listen(20);
        serve = Serve();
    }
    public IConfigurationRoot Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Satellite:StateDirectory"] = DirectoryPath,
        ["Satellite:NetworkSocket"] = SocketPath,
        ["Satellite:KioskState"] = Path.Combine(DirectoryPath, "kiosk.json")
    }).Build();
    public async Task<JsonElement> Run(object request)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/python3")
        { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false } };
        foreach (var arg in new[] { "-I", Path.Combine(repository, "pi-satellite/tests/agent_broker_fixture.py"), repository, DirectoryPath }) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0) throw new IOException("Synthetic broker process failed: " + await errors);
        using var value = JsonDocument.Parse(await output); return value.RootElement.Clone();
    }
    private async Task Serve()
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                using var peer = await server.AcceptAsync(stopping.Token);
                using var stream = new NetworkStream(peer);
                using var reader = new StreamReader(stream, leaveOpen: true);
                using var request = JsonDocument.Parse((await reader.ReadLineAsync(stopping.Token))!);
                var action = request.RootElement.GetProperty("action").GetString();
                if (action == "rebind")
                {
                    RebindRequests++;
                    if (RejectNextRebind)
                    {
                        RejectNextRebind = false;
                        await stream.WriteAsync("{\"ok\":false}\n"u8.ToArray(), stopping.Token);
                        continue;
                    }
                }
                if (action == "reset") ResetRequests++;
                var reply = await Run(request.RootElement);
                if (action == "rebind" && reply.GetProperty("ok").GetBoolean())
                {
                    if (FailAgentSaveAfterRebind)
                    {
                        FailAgentSaveAfterRebind = false;
                        File.Move(Path.Combine(DirectoryPath, "agent.json"), Path.Combine(DirectoryPath, "agent-before-save.json"));
                        System.IO.Directory.CreateDirectory(Path.Combine(DirectoryPath, "agent.json"));
                    }
                    if (DropNextRebindReply) { DropNextRebindReply = false; continue; }
                }
                if (action == "reset" && DropNextResetReply) { DropNextResetReply = false; continue; }
                await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(reply), stopping.Token);
                await stream.WriteAsync("\n"u8.ToArray(), stopping.Token);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (stopping.IsCancellationRequested) { break; }
        }
    }
    public void RestoreInterruptedAgentFile()
    {
        System.IO.Directory.Delete(Path.Combine(DirectoryPath, "agent.json"));
        File.Move(Path.Combine(DirectoryPath, "agent-before-save.json"), Path.Combine(DirectoryPath, "agent.json"));
    }
    public async ValueTask DisposeAsync()
    {
        stopping.Cancel(); server.Dispose(); await serve; stopping.Dispose();
    }
}

internal sealed class SyntheticCoordinationHub : IAsyncDisposable
{
    private readonly WebApplicationFactory<EncounterStateService> factory;
    private readonly WebApplication server;
    private readonly X509Certificate2 certificate;
    public DeviceCoordinationService Coordination => factory.Services.GetRequiredService<DeviceCoordinationService>();
    public string Origin { get; }
    public string Pin => DeviceProtocol.Fingerprint(certificate);
    public Action<string>? BeforeResponse { get; set; }
    private SyntheticCoordinationHub(WebApplicationFactory<EncounterStateService> factory, WebApplication server, X509Certificate2 certificate, string origin)
    { this.factory = factory; this.server = server; this.certificate = certificate; Origin = origin; }
    public static async Task<SyntheticCoordinationHub> Start(string directory)
    {
        var factory = new WebApplicationFactory<EncounterStateService>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development").UseSetting("Tracker:DataDirectory", directory).UseSetting("Coordination:Enabled", "true");
            b.ConfigureLogging(log => log.ClearProviders());
            b.ConfigureServices(services => { services.RemoveAll<ICoordinationNetwork>(); services.AddSingleton<ICoordinationNetwork, SyntheticNetwork>(); });
        });
        var service = factory.Services.GetRequiredService<DeviceCoordinationService>();
        var certificate = DeviceProtocol.Certificate(Path.Combine(directory, "endpoint.pfx"), "Synthetic pairing hub", true);
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.Configuration["Kestrel:Endpoints:LanHttp:Url"] = "http://127.0.0.1:0";
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(tls =>
        {
            tls.ServerCertificate = certificate; tls.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
            tls.ClientCertificateValidation = (cert, _, _) => cert.NotBefore.ToUniversalTime() <= DateTime.UtcNow && cert.NotAfter.ToUniversalTime() > DateTime.UtcNow;
        })));
        builder.Services.AddSignalR(); builder.Services.AddSingleton(service);
        builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, DeviceCertificateAuthentication>("DeviceCertificate", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options => options.AddConcurrencyLimiter("device-enrollment", limiter => { limiter.PermitLimit = 20; limiter.QueueLimit = 0; }));
        var server = builder.Build();
        SyntheticCoordinationHub? result = null;
        server.Use(async (context, next) => { result?.BeforeResponse?.Invoke(context.Request.Path); await next(); });
        server.UseRouting(); server.UseRateLimiter(); server.UseAuthentication(); server.UseAuthorization(); CoordinationEndpoints.Map(server);
        await server.StartAsync();
        var origin = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single(x => x.StartsWith("https://", StringComparison.Ordinal));
        result = new(factory, server, certificate, origin); return result;
    }
    public void Approve(SatelliteAgent agent)
    {
        var enrollment = Coordination.Snapshot.Enrollments.Single(x => x.DeviceId == agent.Snapshot.DeviceId);
        Coordination.Approve(enrollment.DeviceId, agent.Pin, DateTimeOffset.UtcNow, null, enrollment.IntentId);
    }
    public async ValueTask DisposeAsync() { await server.DisposeAsync(); factory.Dispose(); certificate.Dispose(); }
    private sealed class SyntheticNetwork : ICoordinationNetwork
    {
        public Task<(WifiProfile Target, WifiProfile Recovery)> ProfilesAsync(string kind, string? uuid, CancellationToken ct) =>
            Task.FromResult((new WifiProfile("Synthetic destination", "synthetic-destination"), new WifiProfile("Synthetic private hub", "synthetic-recovery")));
        public Task<bool> ScheduleAsync(Transition transition, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> ConfirmAsync(Transition transition, CancellationToken ct) => Task.FromResult(true);
    }
}

internal sealed class PairingClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}
