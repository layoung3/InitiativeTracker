using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using InitiativeTracker.Core.Coordination;
using InitiativeTracker.Satellite;
using InitiativeTracker.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InitiativeTracker.Tests;

public sealed class SatelliteProvisioningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualTlsOwnerRoutesAndAgentBrokerReassignAToBToAWithFreshDestinationApproval(bool headless)
    {
        await using var fixture = new PhoneFixture(headless);
        await using var first = await SyntheticCoordinationHub.Start(Path.Combine(fixture.DirectoryPath, "hub-a"));
        await using var second = await SyntheticCoordinationHub.Start(Path.Combine(fixture.DirectoryPath, "hub-b"));
        await fixture.OpenWindow(); await fixture.Authorize();
        var firstBinding = await Assign(first);
        await fixture.Cleanup();
        await fixture.OpenWindow(); await fixture.Authorize();
        var secondBinding = await Assign(second);
        Assert.NotEqual(firstBinding.Generation, secondBinding.Generation);
        await first.Coordination.PrepareAsync("wifi", Guid.NewGuid().ToString(), default);
        var oldStatus = first.Coordination.Status(fixture.Agent.Certificate);
        await fixture.Agent.Process(oldStatus, default);
        Assert.Equal(secondBinding, fixture.Agent.Snapshot.Binding);
        var stale = await fixture.Broker.Run(new { action = "prepare", generation = firstBinding.Generation, id = Guid.NewGuid(), revision = 100,
            expires = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()/1000.0, target = new WifiProfile("Old hub", "synthetic-only") });
        Assert.False(stale.GetProperty("ok").GetBoolean());
        first.Coordination.Postpone(first.Coordination.Snapshot.Transition!.Id);
        await fixture.Cleanup();
        await fixture.OpenWindow(); await fixture.Authorize();
        var returned = await Assign(first);
        Assert.NotEqual(firstBinding.Generation, returned.Generation);
        Assert.Equal(firstBinding.HubId, returned.HubId);
        Assert.Equal(returned.Generation, (await fixture.Broker.Run(new { action = "status" })).GetProperty("state").GetProperty("generation").GetGuid());
        Assert.DoesNotContain(fixture.Owner, File.ReadAllText(Path.Combine(fixture.AgentDirectory, "kiosk.json")));

        async Task<DeviceBinding> Assign(SyntheticCoordinationHub hub)
        {
            var previous = fixture.Agent.Snapshot.Binding;
            var response = await fixture.Post("reassign", ("url", hub.Origin), ("pin", hub.Pin), ("name", "Synthetic satellite"),
                ("ssid", "Synthetic destination"), ("password", "synthetic-only-password"));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            await fixture.Agent.Tick(default); // Actual enrollment over pinned mutual TLS, before simulated physical DM approval.
            var pending = hub.Coordination.Snapshot.Enrollments.Single(x => x.DeviceId == fixture.Agent.Snapshot.DeviceId);
            Assert.False(pending.Approved);
            Assert.Equal(previous, fixture.Agent.Snapshot.Binding);
            Assert.Equal(HttpStatusCode.Conflict, (await fixture.Post("finish")).StatusCode);
            hub.Approve(fixture.Agent); // Same service operation as the independently tested physical DM approval route.
            Assert.Equal(HttpStatusCode.Redirect, (await fixture.Post("finish")).StatusCode);
            Assert.Null(fixture.Agent.Snapshot.Candidate); Assert.Null(fixture.Agent.Snapshot.PendingBinding);
            return fixture.Agent.Snapshot.Binding!;
        }
    }

    [Fact]
    public async Task OwnerHttpsRejectsWrongProofCsrfOriginHostAndExpiredWindowWithoutChangingTrust()
    {
        await using var fixture = new PhoneFixture(false);
        await fixture.OpenWindow();
        using var stranger = fixture.PhoneClient();
        var login = await stranger.GetStringAsync("/");
        Assert.Contains("Owner authorization", login);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Post(stranger, "authorize", ("owner", new string('0', 64)), ("code", fixture.Code))).StatusCode);
        var authorized = await fixture.Authorize();
        Assert.Contains("secure", authorized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", authorized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", authorized, StringComparison.OrdinalIgnoreCase);
        var badCsrf = new FormUrlEncodedContent(new Dictionary<string, string> { ["csrf"] = "wrong" });
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Phone.PostAsync("/forget", badCsrf)).StatusCode);
        fixture.Phone.DefaultRequestHeaders.Remove("Origin"); fixture.Phone.DefaultRequestHeaders.Add("Origin", "https://attacker.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Post("forget")).StatusCode);
        fixture.Phone.DefaultRequestHeaders.Remove("Origin"); fixture.Phone.DefaultRequestHeaders.Add("Origin", fixture.PhoneOrigin);
        fixture.Phone.DefaultRequestHeaders.Host = "attacker.invalid:" + new Uri(fixture.PhoneOrigin).Port;
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Phone.GetAsync("/")).StatusCode);
        fixture.Phone.DefaultRequestHeaders.Host = null;
        Assert.Null(fixture.Agent.Snapshot.Binding);
        // The successful proof consumed this window's screen code; it cannot mint another session.
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Post(stranger, "authorize", ("owner", fixture.Owner), ("code", fixture.Code))).StatusCode);
        Assert.True((await fixture.Broker.Run(new { action = "__fixture-expire-lease" })).GetProperty("ok").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Phone.GetAsync("/")).StatusCode);
        await fixture.OpenWindow();
        Assert.Contains("Owner authorization", await fixture.Phone.GetStringAsync("/")); // Prior cookie belongs to the prior lease.
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Post("forget")).StatusCode);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Post(stranger, "authorize", ("owner", new string('0', 64)), ("code", fixture.Code))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Post(stranger, "authorize", ("owner", fixture.Owner), ("code", fixture.Code))).StatusCode);
        Assert.Equal(0, fixture.Broker.ResetRequests);
        Assert.Null(fixture.Agent.Snapshot.Binding);
    }

    [Fact]
    public async Task CandidateApprovalCannotOutliveItsOriginalProvisioningLease()
    {
        await using var fixture = new PhoneFixture(false);
        await using var hub = await SyntheticCoordinationHub.Start(Path.Combine(fixture.DirectoryPath, "hub"));
        await fixture.OpenWindow(); await fixture.Authorize();
        Assert.Equal(HttpStatusCode.Redirect, (await fixture.Post("reassign", ("url", hub.Origin), ("pin", hub.Pin), ("name", "Synthetic satellite"),
            ("ssid", "Synthetic destination"), ("password", "synthetic-only-password"))).StatusCode);
        var state = (await fixture.Broker.Run(new { action = "status" })).GetProperty("state");
        Assert.Equal(state.GetProperty("provisioning").GetProperty("id").GetGuid(), fixture.Agent.Snapshot.Candidate!.LeaseId);
        var deadline = state.GetProperty("provisioning").GetProperty("deadline").GetDouble();
        Assert.True(fixture.Agent.Snapshot.Candidate.ExpiresUtc.ToUnixTimeMilliseconds()/1000.0 <= deadline);
        await fixture.Agent.Tick(default); hub.Approve(fixture.Agent);
        await fixture.Broker.Run(new { action = "__fixture-expire-lease" });
        // Even a slow agent clock cannot use the former lease after the root timer ended it.
        await Assert.ThrowsAsync<IOException>(() => fixture.Agent.FinishPairing(default));
        Assert.Null(fixture.Agent.Snapshot.Binding);
        Assert.Equal(JsonValueKind.Null, (await fixture.Broker.Run(new { action = "status" })).GetProperty("state").GetProperty("generation").ValueKind);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("wrong-host")]
    public async Task SatelliteActualTlsRejectsInvalidPhoneCertificateWithoutHttpSecretFallback(string certificateKind)
    {
        await using var fixture = new PhoneFixture(false, certificateKind);
        using var client = fixture.PhoneClient();
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("/"));
        var html = await fixture.Local.GetStringAsync("/");
        Assert.DoesNotContain("name='password'", html);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Local.PostAsync("/network", new FormUrlEncodedContent(new Dictionary<string, string> { ["csrf"] = fixture.Csrf }))).StatusCode);
    }

    private sealed class PhoneFixture : IAsyncDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "n8-phone-" + Guid.NewGuid());
        public string AgentDirectory => Path.Combine(DirectoryPath, "agent");
        public NativeSatelliteBroker Broker { get; }
        public WebApplicationFactory<SatelliteAgent> Factory { get; }
        public SatelliteAgent Agent => Factory.Services.GetRequiredService<SatelliteAgent>();
        public HttpClient Local { get; }
        public HttpClient Phone { get; }
        public string PhoneOrigin { get; }
        public string Owner { get; }
        public string Code { get; private set; } = "";
        public string Csrf { get; private set; } = "";
        private readonly PhoneTls tls;
        private readonly bool headless;
        public PhoneFixture(bool headless, string certificateKind = "valid")
        {
            this.headless = headless; Broker = new(AgentDirectory); tls = new(certificateKind);
            Factory = new WebApplicationFactory<SatelliteAgent>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development").UseSetting("Satellite:StateDirectory", AgentDirectory).UseSetting("Satellite:NetworkSocket", Broker.SocketPath)
                    .UseSetting("Satellite:KioskState", Path.Combine(AgentDirectory, "kiosk.json")).UseSetting("Satellite:PhoneHost", PhoneTls.Name)
                    .UseSetting("Satellite:Headless", headless.ToString()).UseSetting("Kestrel:Endpoints:LanHttp:Url", "http://127.0.0.1:0");
                builder.ConfigureLogging(log => log.ClearProviders());
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IProvisioningHttps>(); services.AddSingleton<IProvisioningHttps>(tls);
                    foreach (var service in services.Where(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(SatelliteWorker)).ToArray()) services.Remove(service);
                });
            });
            Factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(tls.Certificate)));
            Factory.StartServer();
            var addresses = Factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
            var port = new Uri(addresses.Single(x => x.StartsWith("https://", StringComparison.Ordinal))).Port;
            PhoneOrigin = $"https://{PhoneTls.Name}:{port}";
            Local = new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri("http://localhost:5081"), Timeout = TimeSpan.FromSeconds(15) };
            Local.DefaultRequestHeaders.Add("Origin", "http://localhost:5081");
            Phone = PhoneClient(); Owner = Agent.SetupOwnerCredential!;
        }
        public HttpClient PhoneClient()
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseProxy = false,
                ConnectCallback = async (context, cancellation) =>
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    await socket.ConnectAsync(IPAddress.Loopback, context.DnsEndPoint.Port, cancellation);
                    return new NetworkStream(socket, ownsSocket: true);
                },
                SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate is X509Certificate2 cert && tls.Validate(cert) }
            };
            var client = new HttpClient(handler) { BaseAddress = new Uri(PhoneOrigin), Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Add("Origin", PhoneOrigin); return client;
        }
        public async Task OpenWindow()
        {
            var html = await Local.GetStringAsync("/");
            Csrf = Regex.Match(html, "name='csrf' value='([^']+)'").Groups[1].Value;
            Assert.Equal(HttpStatusCode.Redirect, (await Local.PostAsync("/setup", new FormUrlEncodedContent(new Dictionary<string, string> { ["csrf"] = Csrf }))).StatusCode);
            html = await Local.GetStringAsync("/");
            Code = Regex.Match(html, "One-time phone authorization code: <strong>([0-9]+)</strong>").Groups[1].Value;
            Assert.Equal(8, Code.Length);
        }
        public async Task<string> Authorize()
        {
            var page = await Phone.GetStringAsync("/");
            Assert.Contains("Owner authorization", page);
            Assert.Equal(!headless, page.Contains("name='code'", StringComparison.Ordinal));
            var response = await Post(Phone, "authorize", ("owner", Owner), ("code", headless ? "" : Code));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("Satellite Setup", await Phone.GetStringAsync("/"));
            return response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("IT.Setup="));
        }
        public Task<HttpResponseMessage> Post(string action, params (string Key, string Value)[] fields) => Post(Phone, action, fields);
        public Task<HttpResponseMessage> Post(HttpClient client, string action, params (string Key, string Value)[] fields) => client.PostAsync("/" + action,
            new FormUrlEncodedContent(fields.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)).Append(new("csrf", Csrf))));
        public async Task Cleanup() { for (var i = 0; i < 6; i++) await Broker.Run(new { action = "__fixture-tick" }); }
        public async ValueTask DisposeAsync()
        {
            Phone.Dispose(); Local.Dispose(); await Factory.DisposeAsync(); await Broker.DisposeAsync(); tls.Dispose();
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
        }
    }

    // Explicit in-memory fixture CA trust on both sides; never OS trust installation or accept-any TLS.
    private sealed class PhoneTls : IProvisioningHttps, IDisposable
    {
        public const string Name = "satellite.synthetic.invalid";
        public string Host => Name;
        private readonly RSA rootKey = RSA.Create(2048), key = RSA.Create(2048);
        private readonly X509Certificate2 root;
        public X509Certificate2 Certificate { get; }
        public X509Certificate2Collection Chain { get; } = [];
        public bool Ready => Validate(Certificate);
        public PhoneTls(string kind)
        {
            var now = DateTimeOffset.UtcNow;
            var request = new CertificateRequest("CN=Synthetic phone fixture CA", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            root = request.CreateSelfSigned(now.AddDays(-3), now.AddDays(3));
            var leaf = new CertificateRequest("CN=Synthetic phone endpoint", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder(); san.AddDnsName(kind == "wrong-host" ? "wrong.synthetic.invalid" : Name); leaf.CertificateExtensions.Add(san.Build());
            leaf.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
            using var signed = leaf.Create(root, now.AddDays(-2), kind == "expired" ? now.AddDays(-1) : now.AddDays(1), RandomNumberGenerator.GetBytes(16));
            Certificate = signed.CopyWithPrivateKey(key);
        }
        public bool Validate(X509Certificate2 certificate)
        {
            if (!certificate.MatchesHostname(Name, false, false)) return false;
            using var chain = new X509Chain(); chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(root); chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.DisableCertificateDownloads = true; chain.ChainPolicy.ApplicationPolicy.Add(new("1.3.6.1.5.5.7.3.1"));
            return chain.Build(certificate);
        }
        public void Dispose() { Certificate.Dispose(); root.Dispose(); key.Dispose(); rootKey.Dispose(); }
    }
}
