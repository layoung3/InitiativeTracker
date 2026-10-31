using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using InitiativeTracker.Core.Coordination;
using InitiativeTracker.Satellite;
using Microsoft.Extensions.Configuration;
namespace InitiativeTracker.Tests;

public sealed class SatelliteRecoveryTests
{
    [Fact]
    public async Task DisconnectedAgentUsesActualBrokerThenOffersBoundedApAfterUnverifiedTrialSettles()
    {
        var directory=Path.Combine(Path.GetTempPath(),"n8-recovery-"+Guid.NewGuid()); Directory.CreateDirectory(directory);
        var repository=new DirectoryInfo(AppContext.BaseDirectory);
        while(repository is not null && !File.Exists(Path.Combine(repository.FullName,"InitiativeTracker.slnx"))) repository=repository.Parent;
        Assert.NotNull(repository);
        async Task<JsonElement> RunBroker(object request)
        {
            using var process=new Process {StartInfo=new ProcessStartInfo("/usr/bin/python3")
            {RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false}};
            foreach(var argument in new[]{"-I",Path.Combine(repository.FullName,"pi-satellite/tests/agent_broker_fixture.py"),repository.FullName,directory}) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request,new JsonSerializerOptions(JsonSerializerDefaults.Web)));process.StandardInput.Close();
            var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(); Assert.True(process.ExitCode==0,await errors);
            using var document=JsonDocument.Parse(await output);return document.RootElement.Clone();
        }
        using var stopping=new CancellationTokenSource();
        using var server=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
        var path=Path.Combine(directory,"network.sock");server.Bind(new UnixDomainSocketEndPoint(path));server.Listen(10);
        var recoveryRequests=0;
        var serve=Task.Run(async ()=>
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
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            { ["Satellite:StateDirectory"]=directory,["Satellite:NetworkSocket"]=path,["Satellite:KioskState"]=Path.Combine(directory,"kiosk.json"),
              ["Satellite:RecoveryHubHost"]="127.0.0.1",["Satellite:PhoneCertificate"]="test-configured-provider"}).Build();
            var clock=new Clock();await using var initial=new SatelliteAgent(config,clock);
            var binding=new DeviceBinding(Guid.NewGuid(),initial.Snapshot.DeviceId,Guid.NewGuid(),new string('A',64),"https://127.0.0.1:1","http://synthetic.invalid/display");
            Assert.True((await RunBroker(new{action="bootstrap",generation=binding.Generation,recovery=new WifiProfile("synthetic-recovery","synthetic-only")})).GetProperty("ok").GetBoolean());
            PrivateFile.Save(Path.Combine(directory,"agent.json"),initial.Snapshot with{Binding=binding});
            await using var agent=new SatelliteAgent(config,clock);
            for(var i=0;i<6;i++){await agent.Tick(default);clock.Advance(TimeSpan.FromSeconds(5));}
            Assert.Equal(0,recoveryRequests);
            clock.Advance(TimeSpan.FromSeconds(89));await agent.Tick(default);Assert.Equal(0,recoveryRequests);
            clock.Advance(TimeSpan.FromSeconds(1));await agent.Tick(default);Assert.Equal(1,recoveryRequests);
            var armed=await RunBroker(new{action="status"});
            Assert.Equal(JsonValueKind.Null,armed.GetProperty("state").GetProperty("transition").GetProperty("previous").ValueKind);
            var trial=await RunBroker(new{action="__fixture-tick"});
            Assert.Equal(trial.GetProperty("state").GetProperty("recovery").GetString(),trial.GetProperty("network").GetProperty("active").GetString());
            clock.Advance(TimeSpan.FromMinutes(4));await agent.Tick(default);
            Assert.Equal(JsonValueKind.Null,(await RunBroker(new{action="status"})).GetProperty("state").GetProperty("provisioning").ValueKind);
            var expired=await RunBroker(new{action="__fixture-expire"});
            Assert.Equal(JsonValueKind.Null,expired.GetProperty("network").GetProperty("active").ValueKind);
            await agent.Tick(default);
            var setup=await RunBroker(new{action="status"});
            Assert.Equal("recovered",setup.GetProperty("state").GetProperty("transition").GetProperty("phase").GetString());
            Assert.Equal("active",setup.GetProperty("state").GetProperty("provisioning").GetProperty("phase").GetString());
            Assert.Equal(1,recoveryRequests);
            Assert.Equal(binding,agent.Snapshot.Binding);Assert.Null(agent.Snapshot.Candidate);
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
