using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using InitiativeTracker.Satellite;
using Microsoft.Extensions.Configuration;
using InitiativeTracker.Core.Coordination;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace InitiativeTracker.Tests;

public sealed class CoordinationTlsTests
{
    [Fact]
    public async Task RealTlsSignalRRequiresIndependentDeviceCertificateAndRechecksRevocation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "n8-tls-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using var factory = new WebApplicationFactory<EncounterStateService>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development").UseSetting("Tracker:DataDirectory", directory).UseSetting("Coordination:Enabled", "true");
                builder.ConfigureServices(services => { services.RemoveAll<ICoordinationNetwork>(); services.AddSingleton<ICoordinationNetwork, TestNetwork>(); });
            });
            var service = factory.Services.GetRequiredService<DeviceCoordinationService>();
            using var serverCertificate = DeviceProtocol.Certificate(Path.Combine(directory,"test-server.pfx"),"Synthetic TLS server",true);
            using var clientCertificate = DeviceProtocol.Certificate(Path.Combine(directory,"test-agent.pfx"),"Synthetic TLS agent",false);
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Configuration["Kestrel:Endpoints:LanHttp:Url"] = "http://127.0.0.1:0";
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback,0, listen => listen.UseHttps(tls =>
            {
                tls.ServerCertificate = serverCertificate; tls.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                tls.ClientCertificateValidation = (_,_,_) => true; // Synthetic fixture; route authorization still requires enrolled exact pin.
            })));
            builder.Services.AddSignalR(); builder.Services.AddSingleton(service);
            builder.Services.AddAuthentication().AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,DeviceCertificateAuthentication>("DeviceCertificate",_=>{});
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(options => options.AddConcurrencyLimiter("device-enrollment", limit => { limit.PermitLimit = 20; limit.QueueLimit = 0; }));
            await using var server = builder.Build(); server.UseRouting(); server.UseRateLimiter(); server.UseAuthentication(); server.UseAuthorization(); CoordinationEndpoints.Map(server); await server.StartAsync();
            var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First(x => x.StartsWith("https://127.0.0.1:", StringComparison.Ordinal));
            using var handler = new HttpClientHandler(); handler.ClientCertificates.Add(clientCertificate);
            handler.ServerCertificateCustomValidationCallback = (_,cert,_,_) => DeviceProtocol.PinMatches(cert,DeviceProtocol.Fingerprint(serverCertificate));
            using var client = new HttpClient(handler);
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(address+"/api/coordination/status")).StatusCode);
            var request = new EnrollmentRequest(Guid.NewGuid(),"Synthetic satellite",1,DeviceProtocol.Capabilities, Guid.NewGuid());
            var response = await client.PostAsJsonAsync(address+"/api/coordination/enroll",request);
            response.EnsureSuccessStatusCode();
            var enrollment = (await response.Content.ReadFromJsonAsync<Enrollment>())!;
            service.Approve(enrollment.DeviceId,DeviceProtocol.Fingerprint(clientCertificate),DateTimeOffset.UtcNow);
            await service.PrepareAsync("wifi",Guid.NewGuid().ToString(),default);
            await using var signalr = new HubConnectionBuilder().WithUrl(address+"/hubs/device-coordination",options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.ClientCertificates = new() { clientCertificate };
                options.HttpMessageHandlerFactory = h =>
                {
                    ((HttpClientHandler)h).ClientCertificates.Add(clientCertificate);
                    ((HttpClientHandler)h).ServerCertificateCustomValidationCallback = (_,cert,_,_) => DeviceProtocol.PinMatches(cert,DeviceProtocol.Fingerprint(serverCertificate));
                    return h;
                };
            }).Build();
            await signalr.StartAsync();
            var status = await signalr.InvokeAsync<DeviceStatus>("Poll");
            Assert.Equal(enrollment.Generation,status.Generation);
            Assert.Equal("synthetic-only-secret",status.Plan!.Target.Password);
            await signalr.InvokeAsync("Receipt",new DeviceReceipt(1,status.Plan.Transition.Id,status.Plan.Transition.Revision,enrollment.Generation,"ready"));
            Assert.True(service.Snapshot.Transition!.Devices[0].Ready);
            await ExerciseSatelliteAgent(directory,address,status.HubId,enrollment,service);
            service.Revoke(enrollment.DeviceId);
            await Assert.ThrowsAnyAsync<Exception>(()=>signalr.InvokeAsync<DeviceStatus>("Poll"));
            Assert.Throws<UnauthorizedAccessException>(()=>service.Status(clientCertificate));
            using var badPin = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_,cert,_,_) => DeviceProtocol.PinMatches(cert,new string('0',64)) });
            await Assert.ThrowsAsync<HttpRequestException>(()=>badPin.GetAsync(address+"/api/coordination/status"));
            await server.StopAsync();
        }
        finally { Directory.Delete(directory,true); }
    }
    private static async Task ExerciseSatelliteAgent(string directory,string address,Guid hubId,Enrollment enrollment,DeviceCoordinationService service)
    {
        var agentDirectory=Path.Combine(directory,"satellite"); Directory.CreateDirectory(agentDirectory);
        File.Copy(Path.Combine(directory,"test-agent.pfx"),Path.Combine(agentDirectory,"agent.pfx"));
        var socketPath=Path.Combine(agentDirectory,"network.sock");
        using var socket=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(socketPath)); socket.Listen(10);
        using var stopping=new CancellationTokenSource();
        var target=Guid.NewGuid().ToString(); var recovery=Guid.NewGuid().ToString(); var active=Guid.NewGuid().ToString();
        string? transition=null; string phase="none"; var staged=false;
        var broker=Task.Run(async ()=>
        {
            while(!stopping.IsCancellationRequested)
            {
                try
                {
                    using var peer=await socket.AcceptAsync(stopping.Token); using var stream=new NetworkStream(peer); using var reader=new StreamReader(stream,leaveOpen:true);
                    using var request=JsonDocument.Parse((await reader.ReadLineAsync(stopping.Token))!);
                    var action=request.RootElement.GetProperty("action").GetString();
                    if(action=="bootstrap") staged=true;
                    if(action=="prepare") {Assert.True(staged);transition=request.RootElement.GetProperty("id").GetString();phase="prepared";}
                    if(action=="commit") {Assert.Equal("prepared",phase);phase="committed";active=target;}
                    if(action=="finalize") {Assert.Equal(target,active);phase="finalized";}
                    object reply=action=="status" ? new {ok=true,active,state=new {recovery,provisioning=(object?)null,
                        transition=transition is null ? null : (object)new {id=transition,target,phase}}} : new {ok=true};
                    await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(reply),stopping.Token); await stream.WriteAsync("\n"u8.ToArray(),stopping.Token);
                }
                catch(OperationCanceledException) {break;}
                catch(ObjectDisposedException) {break;}
            }
        });
        try
        {
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            { ["Satellite:StateDirectory"]=agentDirectory,["Satellite:NetworkSocket"]=socketPath,["Satellite:KioskState"]=Path.Combine(agentDirectory,"kiosk.json")}).Build();
            await using var initial=new SatelliteAgent(config);
            PrivateFile.Save(Path.Combine(agentDirectory,"agent.json"),initial.Snapshot with
            {DeviceId=enrollment.DeviceId,Binding=new(hubId,enrollment.DeviceId,enrollment.Generation,
                DeviceProtocol.Fingerprint(DeviceProtocol.Certificate(Path.Combine(directory,"test-server.pfx"),"Synthetic",true)),address,"http://synthetic.invalid/display")});
            await using var agent=new SatelliteAgent(config);
            await agent.Tick(default);
            Assert.Equal("prepared",phase);
            service.Commit(service.Snapshot.Transition!.Id,[],DateTimeOffset.UtcNow.AddSeconds(-11));
            await agent.Tick(default);
            Assert.True(service.Snapshot.Transition.Devices[0].Reconnected);
            Assert.False(service.Snapshot.Transition.Devices[0].Finalized);
            await service.ConfirmAsync(service.Snapshot.Transition.Id,default);
            await agent.Tick(default);
            Assert.Equal("finalized",phase);
            Assert.True(service.Snapshot.Transition.Devices[0].Finalized);
        }
        finally {stopping.Cancel();socket.Dispose();await broker;}
    }
    private sealed class TestNetwork : ICoordinationNetwork
    {
        public Task<(WifiProfile Target,WifiProfile Recovery)> ProfilesAsync(string kind,string? uuid,CancellationToken ct) =>
            Task.FromResult((new WifiProfile("synthetic-ssid","synthetic-only-secret"),new WifiProfile("synthetic-recovery","synthetic-only-secret")));
        public Task<bool> ScheduleAsync(Transition t,CancellationToken ct) => Task.FromResult(true);
        public Task<bool> ConfirmAsync(Transition t,CancellationToken ct) => Task.FromResult(true);
    }
}
