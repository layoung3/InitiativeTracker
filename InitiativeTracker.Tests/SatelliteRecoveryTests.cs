using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using InitiativeTracker.Core.Coordination;
using InitiativeTracker.Satellite;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InitiativeTracker.Tests;

public sealed class SatelliteRecoveryTests
{
    [Fact]
    public async Task ActualAgentAndBrokerRecoverAfterMultipleExpiredApCyclesWhenHubReturns()
    {
        var directory=Path.Combine(Path.GetTempPath(),"n8-recovery-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        var repository=new DirectoryInfo(AppContext.BaseDirectory);
        while(repository is not null&&!File.Exists(Path.Combine(repository.FullName,"InitiativeTracker.slnx")))repository=repository.Parent;
        Assert.NotNull(repository);
        async Task<JsonElement> RunBroker(object request)
        {
            using var process=new Process{StartInfo=new ProcessStartInfo("/usr/bin/python3")
            {RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false}};
            foreach(var argument in new[]{"-I",Path.Combine(repository.FullName,"pi-satellite/tests/agent_broker_fixture.py"),repository.FullName,directory})process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request,new JsonSerializerOptions(JsonSerializerDefaults.Web)));process.StandardInput.Close();
            var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();Assert.True(process.ExitCode==0,await errors);
            using var document=JsonDocument.Parse(await output);return document.RootElement.Clone();
        }
        using var stopping=new CancellationTokenSource();
        using var server=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
        var path=Path.Combine(directory,"network.sock");server.Bind(new UnixDomainSocketEndPoint(path));server.Listen(10);
        var recoveryRequests=0;
        var serve=Task.Run(async()=>
        {
            while(!stopping.IsCancellationRequested)
            {
                try
                {
                    using var socket=await server.AcceptAsync(stopping.Token);using var stream=new NetworkStream(socket);using var reader=new StreamReader(stream,leaveOpen:true);
                    using var request=JsonDocument.Parse((await reader.ReadLineAsync(stopping.Token))!);
                    if(request.RootElement.GetProperty("action").GetString()=="recover-profile")Interlocked.Increment(ref recoveryRequests);
                    var reply=await RunBroker(request.RootElement);
                    await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(reply),stopping.Token);await stream.WriteAsync("\n"u8.ToArray(),stopping.Token);
                }
                catch(OperationCanceledException){break;}
                catch(ObjectDisposedException){break;}
            }
        });
        try
        {
            using var factory=new WebApplicationFactory<EncounterStateService>().WithWebHostBuilder(builder=>builder
                .UseEnvironment("Development").UseSetting("Tracker:DataDirectory",Path.Combine(directory,"hub")).UseSetting("Coordination:Enabled","true"));
            var hub=factory.Services.GetRequiredService<DeviceCoordinationService>();
            using var certificate=DeviceProtocol.Certificate(Path.Combine(directory,"hub/coordination/hub.pfx"),"Synthetic hub",true);
            using var reservation=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);
            reservation.Bind(new IPEndPoint(IPAddress.Loopback,0)); // Bound, not listening: connection fails until simulated radio recovery.
            var port=((IPEndPoint)reservation.LocalEndPoint!).Port;
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            {["Satellite:StateDirectory"]=directory,["Satellite:NetworkSocket"]=path,["Satellite:KioskState"]=Path.Combine(directory,"kiosk.json"),
             ["Satellite:RecoveryHubHost"]="127.0.0.1",["Satellite:PhoneCertificate"]="test-configured-provider"}).Build();
            var clock=new Clock();await using var initial=new SatelliteAgent(config,clock);
            var enrollment=hub.Request(initial.Certificate,new(initial.Snapshot.DeviceId,"Synthetic satellite",1,DeviceProtocol.Capabilities,Guid.NewGuid()),DateTimeOffset.UtcNow);
            hub.Approve(enrollment.DeviceId,enrollment.CertificatePin,DateTimeOffset.UtcNow);
            var hubId=hub.Status(initial.Certificate).HubId;
            var binding=new DeviceBinding(hubId,initial.Snapshot.DeviceId,enrollment.Generation,DeviceProtocol.Fingerprint(certificate),$"https://127.0.0.1:{port}","http://synthetic.invalid/display");
            var managed=Guid.NewGuid().ToString();var networkFile=Path.Combine(directory,"fake-network.json");
            await File.WriteAllTextAsync(networkFile,JsonSerializer.Serialize(new{active=managed,commands=Array.Empty<object>()}));
            Assert.True((await RunBroker(new{action="rebind",generation=binding.Generation,recovery=new WifiProfile("synthetic-recovery","synthetic-only"),intent=Guid.NewGuid(),expires=DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds()/1000.0,lease=(string?)null})).GetProperty("ok").GetBoolean());
            await File.WriteAllTextAsync(networkFile,JsonSerializer.Serialize(new{active=(string?)null,commands=Array.Empty<object>()}));
            PrivateFile.Save(Path.Combine(directory,"agent.json"),initial.Snapshot with{Binding=binding});
            await using var agent=new SatelliteAgent(config,clock);
            for(var i=0;i<6;i++){await agent.Tick(default);clock.Advance(TimeSpan.FromSeconds(5));}
            Assert.Equal(0,recoveryRequests);
            clock.Advance(TimeSpan.FromSeconds(89));await agent.Tick(default);Assert.Equal(0,recoveryRequests);
            clock.Advance(TimeSpan.FromSeconds(1));await agent.Tick(default);Assert.Equal(1,recoveryRequests);
            async Task AtNextRecovery()
            {
                clock.Advance(agent.Snapshot.NextRecoveryUtc!.Value-clock.GetUtcNow()+TimeSpan.FromSeconds(1));
                await agent.Tick(default);
            }
            async Task FailUnverifiedTrial(string kind)
            {
                var armed=await RunBroker(new{action="status"});
                Assert.Equal(kind,armed.GetProperty("state").GetProperty("transition").GetProperty("kind").GetString());
                Assert.Equal(JsonValueKind.Null,armed.GetProperty("state").GetProperty("transition").GetProperty("previous").ValueKind);
                await RunBroker(new{action="__fixture-tick"});
                await agent.Tick(default); // Real pinned HTTPS endpoint is still absent, despite a connected fake SSID.
                var expired=await RunBroker(new{action="__fixture-expire"});
                Assert.Equal(JsonValueKind.Null,expired.GetProperty("network").GetProperty("active").ValueKind);
            }
            for(var cycle=0;cycle<2;cycle++)
            {
                await FailUnverifiedTrial("preferred");
                await AtNextRecovery();await FailUnverifiedTrial("hotspot");
                await AtNextRecovery();
                var setup=await RunBroker(new{action="status"});
                Assert.Equal("active",setup.GetProperty("state").GetProperty("provisioning").GetProperty("phase").GetString());
                var deadline=setup.GetProperty("state").GetProperty("provisioning").GetProperty("deadline").GetDouble();
                await agent.Tick(default);
                Assert.Equal(deadline,(await RunBroker(new{action="status"})).GetProperty("state").GetProperty("provisioning").GetProperty("deadline").GetDouble());
                // No agent call closes this window: a new independent timer process does it.
                var closed=await RunBroker(new{action="__fixture-expire-lease"});
                Assert.Equal(JsonValueKind.Null,closed.GetProperty("network").GetProperty("active").ValueKind);
                Assert.Equal(JsonValueKind.Null,closed.GetProperty("state").GetProperty("last_trial").ValueKind);
                Assert.Equal(binding,agent.Snapshot.Binding);Assert.Null(agent.Snapshot.Candidate);
                await AtNextRecovery(); // Must resume preferred Wi-Fi, rather than opening AP forever.
            }
            var joined=await RunBroker(new{action="__fixture-tick"});
            Assert.Equal(managed,joined.GetProperty("network").GetProperty("active").GetString());
            var builder=WebApplication.CreateBuilder();builder.Logging.ClearProviders();
            builder.Configuration["Kestrel:Endpoints:LanHttp:Url"]="http://127.0.0.1:0";
            builder.WebHost.ConfigureKestrel(options=>options.Listen(IPAddress.Loopback,port,listen=>listen.UseHttps(tls=>
            {tls.ServerCertificate=certificate;tls.ClientCertificateMode=ClientCertificateMode.AllowCertificate;tls.ClientCertificateValidation=(_,_,_)=>true;})));
            builder.Services.AddSignalR();builder.Services.AddSingleton(hub);
            builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions,DeviceCertificateAuthentication>("DeviceCertificate",_=>{});
            builder.Services.AddAuthorization();builder.Services.AddRateLimiter(options=>options.AddConcurrencyLimiter("device-enrollment",limit=>{limit.PermitLimit=20;limit.QueueLimit=0;}));
            await using var online=builder.Build();online.UseRouting();online.UseRateLimiter();online.UseAuthentication();online.UseAuthorization();CoordinationEndpoints.Map(online);
            reservation.Dispose();await online.StartAsync();
            await agent.Tick(default);
            var restored=await RunBroker(new{action="status"});
            Assert.Equal("finalized",restored.GetProperty("state").GetProperty("transition").GetProperty("phase").GetString());
            Assert.Equal(managed,restored.GetProperty("active").GetString());
            Assert.Equal(binding,agent.Snapshot.Binding);Assert.Null(agent.Snapshot.UnavailableSince);Assert.Null(agent.Snapshot.NextRecoveryUtc);
            Assert.Equal(0,agent.Snapshot.RecoveryAttempts);Assert.Equal(5,recoveryRequests);
            await online.StopAsync();
        }
        finally{stopping.Cancel();server.Dispose();await serve;Directory.Delete(directory,true);}
    }
    private sealed class Clock:TimeProvider
    {
        private DateTimeOffset now=DateTimeOffset.UtcNow;private long elapsed;
        public override DateTimeOffset GetUtcNow()=>now;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>elapsed;
        public void Advance(TimeSpan span){now+=span;elapsed+=span.Ticks;}
    }
}
