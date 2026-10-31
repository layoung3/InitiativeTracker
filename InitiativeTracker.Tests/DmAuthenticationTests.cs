using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Text.Json;
using InitiativeTracker.Core.Coordination;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InitiativeTracker.Tests;

public sealed class DmAuthenticationTests
{
    [Fact]
    public async Task RealHttpRejectsPinBeforeAntiforgeryAndBindingIncludingTrailingSlashAndForwardedHeaders()
    {
        using var fixture = new BrowserFixture();
        using var client = fixture.Client(false);
        client.DefaultRequestHeaders.Add("Forwarded", "for=127.0.0.1;proto=https;host=localhost");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "localhost");
        foreach (var route in new[] { "/dm/login", "/DM/LOGIN/", "/dm/login?handler=anything" })
        {
            var response = await client.PostAsync(route, new FormUrlEncodedContent(new Dictionary<string, string> { ["Pin"] = "not-a-credential" }));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); // No antiforgery token: gate precedes Razor binding.
            Assert.Null(response.Headers.Location);
            Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
            Assert.DoesNotContain("not-a-credential", await response.Content.ReadAsStringAsync());
        }
        var html = await client.GetStringAsync("/dm/login");
        Assert.DoesNotContain("name=\"Pin\"", html);
        Assert.Contains(fixture.Https + "/dm/login", html);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/system/health")).StatusCode);
        Assert.NotEqual(HttpStatusCode.MovedPermanently, (await client.GetAsync("/display/pair")).StatusCode);
    }

    [Fact]
    public async Task TrustedTlsPinCreatesDistinctSecureCookieAndPhysicalGrantIsBoundToThatFreshLogin()
    {
        using var fixture = new BrowserFixture();
        using var client = fixture.Client(true);
        var login = await Login(fixture, client);
        Assert.Contains("secure", login, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", login, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", login, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InitiativeTracker.Auth=", login);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/dm")).StatusCode);
        var token = await Token(client, "/dm/network");
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "/dm/network?handler=Postpone", token)).StatusCode);
        var proof = fixture.Factory.Services.GetRequiredService<NetworkAdminAuthority>().PhysicalCode();
        var unlocked = await Post(client, "/dm/network?handler=UnlockNetwork", token, ("NetworkAuthorityCode", proof));
        Assert.Equal(HttpStatusCode.Redirect, unlocked.StatusCode);
        var grant = unlocked.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(NetworkAdminAuthority.CookieName + "="));
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "/dm/network?handler=Postpone", token)).StatusCode);
        // A fresh successful PIN login must obtain a new physical grant, not reuse an old browser grant.
        var reauthenticated = await Post(client, "/dm/login", token, ("Pin", fixture.Factory.Services.GetRequiredService<GameSessionService>().CurrentSession.DmPin));
        Assert.Equal(HttpStatusCode.Redirect, reauthenticated.StatusCode);
        token = await Token(client, "/dm/network");
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "/dm/network?handler=Postpone", token)).StatusCode);
        using var replay = fixture.Client(false);
        replay.DefaultRequestHeaders.Add("Cookie", login.Split(';')[0] + "; " + grant.Split(';')[0]);
        var denied = await replay.GetAsync("/dm");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.DoesNotContain(DmAuthenticationPolicy.SecureCookie, denied.Headers.TryGetValues("Set-Cookie", out var headers) ? string.Join(";", headers) : "");
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(replay, "/dm/network?handler=UnlockNetwork", token, ("NetworkAuthorityCode", proof))).StatusCode);
    }

    [Fact]
    public async Task LegacyGameplayCookieCannotBecomeN8AuthorityOnHttpsOrPhysicalLoopback()
    {
        using var fixture = new BrowserFixture();
        using var client = fixture.Client(true, cookies: false);
        client.DefaultRequestHeaders.Add("Cookie", fixture.LegacyCookie());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/dm")).StatusCode); // Existing gameplay remains usable.
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/dm/network")).StatusCode);
        foreach (var handler in new[] { "UnlockNetwork", "Prepare", "Commit", "ApproveAgent", "RevokeAgent", "ConfirmCoordinated" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/dm/network?handler=" + handler, new StringContent(""))).StatusCode);
        using var http = fixture.Client(false, cookies: false);
        http.DefaultRequestHeaders.Add("Cookie", fixture.LegacyCookie(aged: true));
        var gameplay = await http.GetAsync("/dm");
        Assert.Equal(HttpStatusCode.OK, gameplay.StatusCode);
        Assert.False(gameplay.Headers.TryGetValues("Set-Cookie", out var renewals) && renewals.Any(x => x.StartsWith("InitiativeTracker.Auth=")));
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync("/dm/network?handler=Prepare", new StringContent(""))).StatusCode);
    }

    [Fact]
    public async Task ReadinessLossRejectsExistingSecureSessionAndNoPinOrQrFallsBackToHttp()
    {
        using var fixture = new BrowserFixture();
        using var client = fixture.Client(true);
        await Login(fixture, client);
        fixture.Tls.Available = false;
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/dm")).StatusCode);
        var html = await client.GetStringAsync("/dm/login");
        Assert.DoesNotContain("name=\"Pin\"", html);
        Assert.Contains("not ready", html);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/dm/login", new StringContent(""))).StatusCode);
        using var local = fixture.Client(false);
        Assert.Equal(HttpStatusCode.Forbidden, (await local.GetAsync("/setup/dm-qr")).StatusCode);
        var setup = await local.GetStringAsync("/setup");
        Assert.DoesNotContain("src=\"/setup/dm-qr\"", setup);
        Assert.Contains("Fresh DM login is blocked", setup);
        Assert.DoesNotContain("href=\"/dm/login\"", setup);
    }

    [Fact]
    public async Task SetupUsesConfiguredBrowserOriginAndAgentPortCannotServeBrowserLogin()
    {
        using var fixture = new BrowserFixture();
        using var client = fixture.Client(true);
        var setup = await client.GetStringAsync("/setup");
        Assert.Contains(fixture.Https + "/dm/login", setup);
        Assert.Contains(fixture.Https + "/dm/network", setup);
        Assert.Contains("/display/connect?token=", setup);
        using var badHost = fixture.Client(true);
        badHost.DefaultRequestHeaders.Host = "attacker.invalid:" + fixture.BrowserPort;
        Assert.Equal(HttpStatusCode.NotFound, (await badHost.GetAsync("/setup")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await badHost.PostAsync("/dm/login", new StringContent(""))).StatusCode);
        using var agent = fixture.AgentClient();
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync("/dm/login")).StatusCode);
    }

    [Fact]
    public async Task ExistingDisplayPairingAndEncounterSignalRWorkAlongsideFreshSecureDmAuthentication()
    {
        using var fixture = new BrowserFixture();
        using var display = fixture.Client(false);
        Assert.Equal(HttpStatusCode.OK, (await display.GetAsync("/display/pair")).StatusCode);
        var registry = fixture.Factory.Services.GetRequiredService<DeviceRegistryService>();
        var request = Assert.Single(registry.GetPendingRequests());
        using var dm = fixture.Client(true);
        var cookie = await Login(fixture, dm);
        var token = await Token(dm, "/dm/devices");
        Assert.Equal(HttpStatusCode.Redirect, (await Post(dm, "/dm/devices?handler=Approve", token, ("id", request.Id.ToString()))).StatusCode);
        var approval = await display.GetAsync("/display/pair?handler=Status");
        Assert.Contains("approved", await approval.Content.ReadAsStringAsync());
        Assert.Contains(approval.Headers.GetValues("Set-Cookie"), x => x.StartsWith("InitiativeTracker.Auth="));
        Assert.Equal(HttpStatusCode.OK, (await display.GetAsync("/display")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await display.PostAsync("/dm/network?handler=Prepare", new StringContent(""))).StatusCode);
        var state = fixture.Factory.Services.GetRequiredService<EncounterStateService>();
        state.AddCombatants("Synthetic preserved encounter", 1, 10, 10, 1);
        await using var connection = new HubConnectionBuilder().WithUrl(fixture.Https + "/hubs/encounter", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.Headers["Cookie"] = cookie.Split(';')[0];
            options.HttpMessageHandlerFactory = handler =>
            {
                ((HttpClientHandler)handler).ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert is not null && fixture.ValidateServer(cert);
                return handler;
            };
        }).Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await connection.StartAsync(timeout.Token);
        var initial = await connection.InvokeAsync<JsonElement>("GetEncounterState", timeout.Token);
        Assert.Single(initial.GetProperty("combatants").EnumerateArray());
        var updated = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = connection.On<JsonElement>("EncounterUpdated", value => updated.TrySetResult(value));
        token = await Token(dm, "/dm");
        await Post(dm, "/dm?handler=SetVisibility", token, ("combatantId", state.CurrentEncounter.Combatants[0].Id.ToString()), ("isHiddenFromPublic", "true"));
        Assert.Empty((await updated.Task.WaitAsync(timeout.Token)).GetProperty("combatants").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await display.GetAsync("/display")).StatusCode); // Same persistent display cookie.
    }

    [Fact]
    public async Task SecureAuthenticationDoesNotEchoInvalidPinAndChecksCurrentGameSession()
    {
        using var fixture = new BrowserFixture();
        using var client = fixture.Client(true);
        var token = await Token(client, "/dm/login");
        var response = await Post(client, "/dm/login", token, ("Pin", "invalid-synthetic-proof"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("invalid-synthetic-proof", await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var headers) && headers.Any(x => x.StartsWith(DmAuthenticationPolicy.SecureCookie + "=")));
        await Login(fixture, client);
        fixture.Factory.Services.GetRequiredService<GameSessionService>().CurrentSession.SessionId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/dm")).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("wrong-host")]
    public async Task RealTlsBrowserRejectsInvalidCertificateAndHttpNeverBecomesAuthenticationFallback(string kind)
    {
        using var fixture = new BrowserFixture(kind);
        using var secure = fixture.Client(true);
        await Assert.ThrowsAsync<HttpRequestException>(() => secure.GetAsync("/dm/login"));
        using var http = fixture.Client(false);
        Assert.DoesNotContain("name=\"Pin\"", await http.GetStringAsync("/dm/login"));
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync("/dm/login", new StringContent(""))).StatusCode);
    }

    [Fact]
    public async Task OrdinaryUntrustedClientCannotAuthenticateAndN8DoesNotSetHostnameWideHsts()
    {
        using var fixture = new BrowserFixture(environment: "Production");
        using var untrusted = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        await Assert.ThrowsAsync<HttpRequestException>(() => untrusted.GetAsync(fixture.Https + "/dm/login"));
        using var trusted = fixture.Client(true);
        Assert.False((await trusted.GetAsync("/system/health")).Headers.Contains("Strict-Transport-Security"));
        using var http = fixture.Client(false);
        var page = await http.GetAsync("/display/pair");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync("/dm/login", new StringContent(""))).StatusCode);
    }

    [Fact]
    public async Task N8DisabledLegacyLoginRemainsAvailableWithoutRedirectOrSecureAdministration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "legacy-dm-" + Guid.NewGuid());
        try
        {
            using var factory = new WebApplicationFactory<EncounterStateService>().WithWebHostBuilder(b => b.UseEnvironment("Development").UseSetting("Tracker:DataDirectory", directory));
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var token = await Token(client, "/dm/login");
            var pin = factory.Services.GetRequiredService<GameSessionService>().CurrentSession.DmPin;
            var response = await Post(client, "/dm/login", token, ("Pin", pin));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains(response.Headers.GetValues("Set-Cookie"), x => x.StartsWith("InitiativeTracker.Auth="));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/dm")).StatusCode);
            Assert.Contains("Legacy gameplay compatibility", await factory.CreateClient().GetStringAsync("/dm/login"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("192.0.2.2")]
    [InlineData("2001:db8::2")]
    public void ForgedHeadersAndPortsNeverProvideBrowserTrustOrLoopbackN8Authority(string address)
    {
        using var fixture = new CertificateFixture();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Coordination:Enabled"] = "true" }).Build();
        var policy = new DmAuthenticationPolicy(configuration, new FixtureTls(fixture));
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        context.Connection.LocalPort = 5445; context.Request.Host = new("localhost", 5445);
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["Forwarded"] = "for=127.0.0.1;proto=https;host=localhost:5445";
        Assert.False(policy.PinSubmissionAllowed(context));
        context.Request.Scheme = "https"; Assert.True(policy.TrustedHttps(context));
        context.Connection.LocalPort = 5443; Assert.False(policy.TrustedHttps(context));
        context.Connection.LocalPort = 5445; context.Request.Host = new("localhost", 5443); Assert.False(policy.TrustedHttps(context));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("wrong-host")]
    [InlineData("untrusted")]
    public void ProductionReadinessRejectsInvalidCertificateWithoutInstallingTrust(string kind)
    {
        using var certs = new CertificateFixture(kind);
        var directory = Path.Combine(Path.GetTempPath(), "dm-cert-" + Guid.NewGuid());
        PrivateFile.Directory(directory);
        var path = Path.Combine(directory, "browser.pfx");
        try
        {
            File.WriteAllBytes(path, certs.Server.Export(X509ContentType.Pkcs12));
            Assert.False(new PublicCertificateHttps("localhost", path).Ready);
            Assert.False(new PublicCertificateHttps(null, null).Ready);
            // Ordinary client validation with explicit fixture trust still rejects expiry/name; no accept-any callback.
            if (kind != "untrusted") Assert.False(certs.Validate(certs.Server));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void MalformedConfiguredCertificateFailsStartupInsteadOfHttpAuthenticationFallback()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dm-malformed-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "invalid.pfx"); File.WriteAllText(path, "synthetic invalid pkcs12");
            using var factory = new WebApplicationFactory<EncounterStateService>().WithWebHostBuilder(b => b.UseEnvironment("Development")
                .UseSetting("Tracker:DataDirectory", directory).UseSetting("Coordination:Enabled", "true").UseSetting("BrowserTls:Certificate", path));
            Assert.ThrowsAny<CryptographicException>(() => new PublicCertificateHttps("localhost", path));
            Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task N8CannotDisableHttpsPinRequirementAndLegacyCanOptIntoTheSameMigration(bool n8, bool strict)
    {
        var directory = Path.Combine(Path.GetTempPath(), "strict-dm-" + Guid.NewGuid());
        try
        {
            using var factory = new WebApplicationFactory<EncounterStateService>().WithWebHostBuilder(b => b.UseEnvironment("Development")
                .UseSetting("Tracker:DataDirectory", directory).UseSetting("Coordination:Enabled", n8.ToString())
                .UseSetting("DmAuthentication:RequireTrustedHttps", strict.ToString()));
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            Assert.DoesNotContain("name=\"Pin\"", await client.GetStringAsync("/dm/login"));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/dm/login", new StringContent(""))).StatusCode);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task<string> Login(BrowserFixture fixture, HttpClient client)
    {
        var token = await Token(client, "/dm/login");
        var response = await Post(client, "/dm/login", token, ("Pin", fixture.Factory.Services.GetRequiredService<GameSessionService>().CurrentSession.DmPin));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(DmAuthenticationPolicy.SecureCookie + "="));
    }
    private static async Task<string> Token(HttpClient client, string path)
    {
        var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(Regex.Match(await response.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    }
    private static Task<HttpResponseMessage> Post(HttpClient client, string path, string token, params (string Key, string Value)[] fields) =>
        client.PostAsync(path, new FormUrlEncodedContent(fields.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)).Append(new("__RequestVerificationToken", token))));

    private sealed class BrowserFixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "real-dm-" + Guid.NewGuid());
        private readonly CertificateFixture certificates;
        public WebApplicationFactory<EncounterStateService> Factory { get; }
        public FixtureTls Tls { get; }
        public string Http { get; }
        public string Https { get; }
        public string Agent { get; }
        public int BrowserPort => new Uri(Https).Port;
        public BrowserFixture(string certificateKind = "valid", string environment = "Development")
        {
            certificates = new(certificateKind);
            Tls = new(certificates);
            Factory = new WebApplicationFactory<EncounterStateService>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment).UseSetting("Tracker:DataDirectory", directory).UseSetting("Coordination:Enabled", "true")
                    .UseSetting("Coordination:Port", "0").UseSetting("Kestrel:Endpoints:LanHttp:Url", "http://127.0.0.1:0");
                builder.ConfigureLogging(log => log.ClearProviders());
                builder.ConfigureServices(services => { services.RemoveAll<IProvisioningHttps>(); services.AddSingleton<IProvisioningHttps>(Tls); });
            });
            Factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificates.Server)));
            Factory.StartServer();
            var addresses = Factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
            Http = addresses.Single(x => x.StartsWith("http://", StringComparison.Ordinal));
            Https = addresses.Single(x => x.StartsWith("https://127.0.0.1:", StringComparison.Ordinal)).Replace("127.0.0.1", "localhost");
            Agent = addresses.Single(x => x.StartsWith("https://[::]:", StringComparison.Ordinal)).Replace("[::]", "localhost");
            var config = Factory.Services.GetRequiredService<IConfiguration>();
            config["BrowserTls:Port"] = BrowserPort.ToString(); config["Coordination:Port"] = new Uri(Agent).Port.ToString();
        }
        public HttpClient Client(bool https, bool cookies = true) => new(new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = cookies,
            ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert is not null && certificates.Validate(cert)
        }) { BaseAddress = new Uri(https ? Https : Http), Timeout = TimeSpan.FromSeconds(15) };
        public HttpClient AgentClient()
        {
            using var cert = X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(directory, "coordination", "hub.pfx"), null);
            var pin = DeviceProtocol.Fingerprint(cert);
            return new(new HttpClientHandler { AllowAutoRedirect = false, ServerCertificateCustomValidationCallback = (_, c, _, _) => DeviceProtocol.PinMatches(c, pin) }) { BaseAddress = new Uri(Agent) };
        }
        public bool ValidateServer(X509Certificate2 certificate) => certificates.Validate(certificate);
        public string LegacyCookie(bool aged = false)
        {
            var options = Factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "DM"), new Claim("session_id", Factory.Services.GetRequiredService<GameSessionService>().CurrentSession.SessionId.ToString()) }, CookieAuthenticationDefaults.AuthenticationScheme));
            var properties = new AuthenticationProperties();
            if (aged) { properties.IssuedUtc = DateTimeOffset.UtcNow.AddHours(-7); properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(5); }
            return options.Cookie.Name + "=" + options.TicketDataFormat.Protect(new AuthenticationTicket(principal, properties, CookieAuthenticationDefaults.AuthenticationScheme));
        }
        public void Dispose() { Factory.Dispose(); certificates.Dispose(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class FixtureTls(CertificateFixture certificates) : IProvisioningHttps
    {
        public bool Available { get; set; } = true;
        public bool Ready => Available && certificates.Validate(certificates.Server);
        public string Host => "localhost";
        public X509Certificate2 Certificate => certificates.Server;
        public X509Certificate2Collection Chain { get; } = [];
    }
    private sealed class CertificateFixture : IDisposable
    {
        private readonly RSA rootKey = RSA.Create(2048);
        private readonly RSA serverKey = RSA.Create(2048);
        private readonly X509Certificate2 root;
        public X509Certificate2 Server { get; }
        public CertificateFixture(string kind = "valid")
        {
            var now = DateTimeOffset.UtcNow;
            var ca = new CertificateRequest("CN=Synthetic DM fixture CA", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            ca.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            ca.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            root = ca.CreateSelfSigned(now.AddDays(-3), now.AddDays(3));
            var request = new CertificateRequest("CN=Synthetic DM fixture", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder(); san.AddDnsName(kind == "wrong-host" ? "wrong.synthetic.invalid" : "localhost");
            request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
            using var leaf = request.Create(root, now.AddDays(-2), kind == "expired" ? now.AddDays(-1) : now.AddDays(1), RandomNumberGenerator.GetBytes(16));
            Server = leaf.CopyWithPrivateKey(serverKey);
        }
        public bool Validate(X509Certificate2 certificate)
        {
            if (!certificate.MatchesHostname("localhost", false, false)) return false;
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(root);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.DisableCertificateDownloads = true;
            chain.ChainPolicy.ApplicationPolicy.Add(new("1.3.6.1.5.5.7.3.1"));
            return chain.Build(certificate);
        }
        public void Dispose() { Server.Dispose(); root.Dispose(); rootKey.Dispose(); serverKey.Dispose(); }
    }
}
