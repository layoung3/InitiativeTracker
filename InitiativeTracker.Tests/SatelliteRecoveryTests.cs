using System.Net.Sockets;
using System.Text.Json;
using InitiativeTracker.Core.Coordination;
using InitiativeTracker.Satellite;
using Microsoft.Extensions.Configuration;
namespace InitiativeTracker.Tests;

public sealed class SatelliteRecoveryTests
{
    [Fact]
    public async Task ConfirmedTwoMinuteOutageDebouncesAndPersistsBoundedCooldownWithoutUnpairing()
    {
        var directory=Path.Combine(Path.GetTempPath(),"n8-recovery-"+Guid.NewGuid()); Directory.CreateDirectory(directory);
        using var stopping=new CancellationTokenSource();
        using var server=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
        var path=Path.Combine(directory,"network.sock"); server.Bind(new UnixDomainSocketEndPoint(path)); server.Listen(10);
        var recoveryAttempts=0; var active=Guid.NewGuid().ToString(); var recovery=Guid.NewGuid().ToString();
        var serve=Task.Run(async ()=>
        {
            while(!stopping.IsCancellationRequested)
            {
                try
                {
                    using var socket=await server.AcceptAsync(stopping.Token); using var stream=new NetworkStream(socket); using var reader=new StreamReader(stream,leaveOpen:true);
                    using var request=JsonDocument.Parse((await reader.ReadLineAsync(stopping.Token))!);
                    var action=request.RootElement.GetProperty("action").GetString();
                    if(action=="recover-profile") Interlocked.Increment(ref recoveryAttempts);
                    object reply=action=="status" ? new {ok=true,active,state=new {recovery,transition=(object?)null,provisioning=(object?)null}} : new {ok=true};
                    await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(reply),stopping.Token); await stream.WriteAsync("\n"u8.ToArray(),stopping.Token);
                }
                catch(OperationCanceledException) {break;}
                catch(ObjectDisposedException) {break;}
            }
        });
        try
        {
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            { ["Satellite:StateDirectory"]=directory,["Satellite:NetworkSocket"]=path,["Satellite:KioskState"]=Path.Combine(directory,"kiosk.json")}).Build();
            var clock=new Clock(); var initial=new SatelliteAgent(config,clock);
            var binding=new DeviceBinding(Guid.NewGuid(),initial.Snapshot.DeviceId,Guid.NewGuid(),new string('A',64),"https://127.0.0.1:1","http://synthetic.invalid/display");
            PrivateFile.Save(Path.Combine(directory,"agent.json"),initial.Snapshot with {Binding=binding});
            var agent=new SatelliteAgent(config,clock);
            for(var i=0;i<6;i++) {await agent.Tick(default);clock.Advance(TimeSpan.FromSeconds(5));}
            Assert.Equal(0,recoveryAttempts);
            clock.Advance(TimeSpan.FromSeconds(89)); await agent.Tick(default); Assert.Equal(0,recoveryAttempts);
            clock.Advance(TimeSpan.FromSeconds(1)); await agent.Tick(default); Assert.Equal(1,recoveryAttempts);
            clock.Advance(TimeSpan.FromSeconds(10)); await agent.Tick(default); Assert.Equal(1,recoveryAttempts);
            var restored=new SatelliteAgent(config,clock);
            Assert.Equal(binding,restored.Snapshot.Binding);
            Assert.True(restored.Snapshot.NextRecoveryUtc>clock.GetUtcNow());
            Assert.Null(restored.Snapshot.Candidate);
        }
        finally
        {
            stopping.Cancel(); server.Dispose(); await serve; Directory.Delete(directory,true);
        }
    }
    private sealed class Clock:TimeProvider
    {
        private DateTimeOffset now=DateTimeOffset.UtcNow; private long elapsed;
        public override DateTimeOffset GetUtcNow()=>now;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>elapsed;
        public void Advance(TimeSpan span) {now+=span;elapsed+=span.Ticks;}
    }
}
