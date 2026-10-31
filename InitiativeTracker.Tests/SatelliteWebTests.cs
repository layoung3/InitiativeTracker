using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using InitiativeTracker.Satellite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeTracker.Tests;

public sealed class SatelliteWebTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(),"n8-satellite-web-"+Guid.NewGuid());
    private Socket? socket;
    private Task? broker;
    private readonly CancellationTokenSource stopping = new();
    private WebApplicationFactory<SatelliteAgent> app = null!;
    private HttpClient client = null!;
    private bool open;
    public Task InitializeAsync()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory,"network.sock");
        socket = new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(path)); socket.Listen(10);
        broker = Serve();
        app = new WebApplicationFactory<SatelliteAgent>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development").UseSetting("Satellite:StateDirectory",directory).UseSetting("Satellite:NetworkSocket",path)
                .UseSetting("Satellite:KioskState",Path.Combine(directory,"kiosk.json"));
            builder.ConfigureServices(services=>services.AddSingleton<IStartupFilter,ConnectionFixture>());
        });
        client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect=false });
        return Task.CompletedTask;
    }
    private async Task Serve()
    {
        while(!stopping.IsCancellationRequested)
        {
            try
            {
                using var connection = await socket!.AcceptAsync(stopping.Token);
                using var stream = new NetworkStream(connection);
                using var reader = new StreamReader(stream,leaveOpen:true);
                using var request = JsonDocument.Parse((await reader.ReadLineAsync(stopping.Token))!);
                var action = request.RootElement.GetProperty("action").GetString();
                if(action=="open-provisioning") open=true;
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                object reply = action == "provisioning-info" ? new { ok=true, access=(object?)null,
                    lease=open ? (object)new { created=now, deadline=now+600 } : null } : new { ok=true };
                await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(reply),stopping.Token);
                await stream.WriteAsync("\n"u8.ToArray(),stopping.Token);
            }
            catch(OperationCanceledException) { break; }
            catch(ObjectDisposedException) { break; }
        }
    }
    [Fact]
    public async Task PhysicalTouchscreenControlsWorkButNoPasswordFormOrPostBeforeTrustedHttps()
    {
        var html=await client.GetStringAsync("/");
        Assert.Contains("Satellite Setup",html);
        Assert.Contains("id='keys'",html);
        Assert.DoesNotContain("name='password'",html);
        Assert.Contains("Password submission is blocked",html);
        var token=Regex.Match(html,"name='csrf' value='([^']+)'").Groups[1].Value;
        client.DefaultRequestHeaders.Add("Origin","http://localhost");
        var rejected=await client.PostAsync("/network",new FormUrlEncodedContent(new Dictionary<string,string>{["csrf"]=token,["ssid"]="synthetic",["password"]="synthetic-only"}));
        Assert.Equal(HttpStatusCode.Forbidden,rejected.StatusCode);
        var opened=await client.PostAsync("/setup",new FormUrlEncodedContent(new Dictionary<string,string>{["csrf"]=token}));
        Assert.Equal(HttpStatusCode.Redirect,opened.StatusCode); Assert.True(open);
    }
    [Fact]
    public async Task RemoteHttpAndForgedOriginsCannotResetReassignOrReadRecoveryMaterial()
    {
        client.DefaultRequestHeaders.Add("X-Fixture-Remote","yes");
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/")).StatusCode);
        foreach(var action in new[]{"forget","reassign","authorize","setup"})
            Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsync("/"+action,new StringContent(""))).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Fixture-Remote");
        var html=await client.GetStringAsync("/");
        var token=Regex.Match(html,"name='csrf' value='([^']+)'").Groups[1].Value;
        client.DefaultRequestHeaders.Add("Origin","http://attacker.invalid");
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsync("/forget",new FormUrlEncodedContent(new Dictionary<string,string>{["csrf"]=token}))).StatusCode);
        Assert.False(open);
    }
    public async Task DisposeAsync()
    {
        client.Dispose(); app.Dispose(); stopping.Cancel(); socket?.Dispose();
        if(broker is not null) await broker;
        stopping.Dispose(); Directory.Delete(directory,true);
    }
    private sealed class ConnectionFixture : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context,proceed)=>
            {
                context.Connection.RemoteIpAddress=context.Request.Headers.ContainsKey("X-Fixture-Remote") ? IPAddress.Parse("192.0.2.25") : IPAddress.Loopback;
                return proceed();
            }); next(app);
        };
    }
}
