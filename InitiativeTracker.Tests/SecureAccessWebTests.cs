using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Infrastructure;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace InitiativeTracker.Tests;

public sealed class SecureAccessWebTests
{
    public const string Origin = "https://hub.owner-domain.org:5445";
    public sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan delta) => now += delta;
    }
    public sealed class FixtureOrigin : ITrustedBrowserOrigin
    {
        public bool Enabled => true;
        public string Origin => SecureAccessWebTests.Origin;
        public bool TrustedRequest(HttpContext context) => context.Request.IsHttps && context.Request.Host.ToString() == "hub.owner-domain.org:5445" && context.Connection.LocalPort == 5445;
        public bool Physical(HttpContext context) => TrustedRequest(context) && IPAddress.IsLoopback(context.Connection.RemoteIpAddress!);
    }
    public sealed class FixturePipeline : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                context.Connection.LocalPort = context.Request.IsHttps ? 5445 : 5000;
                context.Connection.LocalIpAddress = IPAddress.Loopback;
                // Test-host-only peer simulation. This header is never read by production code.
                context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers["Fixture-Peer"].FirstOrDefault() ?? "192.168.4.22");
                await following();
            });
            next(app);
            app.UseEndpoints(routes =>
            {
                routes.MapGet("/media/fixture", () => Results.Text("private fixture")).RequireAuthorization(p => p.RequireRole("DM", "Player", "Display"));
                routes.MapPost("/appearance/fixture", () => Results.Text("changed")).RequireAuthorization(p => p.RequireRole("DM", "Player"));
            });
        };
    }
    public sealed class FailureSwitch { public bool Enabled; public SecurePersistenceSync? SyncStage; }
    public sealed class FaultPersistence : SecureGrantPersistence
    {
        private readonly SecureAccessStorage storage;
        private readonly FailureSwitch failure;
        public FaultPersistence(SecureAccessStorage storage, Microsoft.AspNetCore.DataProtection.IDataProtectionProvider protection, FailureSwitch failure)
            : base(storage, protection) { this.storage = storage; this.failure = failure; }
        protected override void SynchronizeDirectory(string directory, SecurePersistenceSync stage)
        {
            if (failure.SyncStage == stage)
            {
                failure.SyncStage = null; // Single deterministic failure, including the final-unlink boundary.
                throw new IOException("Fixture directory synchronization failure.");
            }
            base.SynchronizeDirectory(directory, stage);
        }
        public override void Write(SecureGrantState state, bool recovery = false)
        {
            if (failure.Enabled)
            {
                var fence = Path.Combine(storage.Root, "mutation.pending");
                File.WriteAllText(fence, "fixture interruption");
                if (OperatingSystem.IsLinux()) File.SetUnixFileMode(fence, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                throw new SecureAccessUnavailable();
            }
            base.Write(state, recovery);
        }
    }
    public sealed class Host : IDisposable
    {
        public readonly string DirectoryPath;
        public readonly Clock Clock;
        public readonly WebApplicationFactory<EncounterStateService> App;
        public readonly FailureSwitch Failure = new();
        public Host(string? directory = null, Clock? clock = null, bool endpoints = true, Action<IServiceCollection>? configure = null)
        {
            DirectoryPath = directory ?? Path.Combine(Path.GetTempPath(), "secure-web-" + Guid.NewGuid());
            Clock = clock ?? new Clock();
            App = new TestApplicationFactory(DirectoryPath, builder => builder
                .UseEnvironment("Production").UseSetting("Logging:LogLevel:Default", "Warning").UseSetting("Tracker:DataDirectory", DirectoryPath)
                .UseSetting("BrowserTls:Enabled", "true").UseSetting("BrowserTls:Host", "hub.owner-domain.org")
                .UseSetting("BrowserTls:Store", Path.Combine(DirectoryPath, "certificates"))
                .ConfigureServices(services =>
                {
                    services.RemoveAll<ITrustedBrowserOrigin>(); services.AddSingleton<ITrustedBrowserOrigin, FixtureOrigin>();
                    services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Clock);
                    services.AddSingleton(Failure);
                    services.RemoveAll<SecureGrantPersistence>(); services.AddSingleton<SecureGrantPersistence, FaultPersistence>();
                    if (endpoints) services.AddSingleton<IStartupFilter, FixturePipeline>();
                    configure?.Invoke(services);
                }));
        }
        public SecureAccessService Access => App.Services.GetRequiredService<SecureAccessService>();
        public IDbContextFactory<InitiativeTrackerDbContext> Factory => App.Services.GetRequiredService<IDbContextFactory<InitiativeTrackerDbContext>>();
        public Session Client(bool physical = false, bool http = false) => new(this, physical, http);
        private bool disposed;
        private bool deleteOnDispose;
        public void Dispose()
        {
            if (disposed) return;
            App.Dispose();
            disposed = true;
            if (deleteOnDispose) Directory.Delete(DirectoryPath, true);
        }
        public void DeleteOnDispose()
        {
            // The test may still have caller-owned contexts in scope. Their
            // disposal must precede host shutdown and filesystem cleanup.
            if (disposed) Directory.Delete(DirectoryPath, true);
            else deleteOnDispose = true;
        }
    }
    public sealed class Session : IDisposable
    {
        public readonly Host Host;
        public readonly HttpClient Client;
        public readonly Dictionary<string, string> Cookies = [];
        public Session(Host host, bool physical, bool http)
        {
            Host = host;
            Client = host.App.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new(http ? "http://legacy-hub.local:5000" : Origin), AllowAutoRedirect = false, HandleCookies = false });
            Client.DefaultRequestHeaders.Add("Fixture-Peer", physical ? "127.0.0.1" : "192.168.4.22");
        }
        public async Task<HttpResponseMessage> Send(string path, HttpContent? content = null, bool origin = true)
        {
            using var request = new HttpRequestMessage(content is null ? HttpMethod.Get : HttpMethod.Post, path) { Content = content };
            if (Cookies.Count > 0) request.Headers.Add("Cookie", string.Join("; ", Cookies.Select(p => p.Key + "=" + p.Value)));
            if (content is not null && origin) request.Headers.Add("Origin", SecureAccessWebTests.Origin);
            var response = await Client.SendAsync(request);
            if (response.Headers.TryGetValues("Set-Cookie", out var values)) foreach (var value in values)
            {
                var pair = value.Split(';')[0].Split('=', 2);
                if (pair.Length == 2) Cookies[pair[0]] = pair[1];
            }
            return response;
        }
        public async Task<string> Token(string page = "/secure-access")
        {
            var response = await Send(page);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return WebUtility.HtmlDecode(Regex.Match(await response.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        }
        public async Task<HttpResponseMessage> Post(string handler, params (string Key, string Value)[] values)
        {
            var token = await Token();
            return await PostToken(handler, token, values);
        }
        public Task<HttpResponseMessage> PostToken(string handler, string token, params (string Key, string Value)[] values) =>
            Send("/secure-access?handler=" + handler, new FormUrlEncodedContent(values.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)).Append(new("__RequestVerificationToken", token))));
        public void Legacy(string role, Guid? profile = null, Guid? player = null, Guid? credential = null, Guid? display = null)
        {
            var session = Host.App.Services.GetRequiredService<GameSessionService>().CurrentSession.SessionId;
            var claims = new List<Claim> { new(ClaimTypes.Role, role), new("session_id", session.ToString()) };
            if (profile.HasValue) claims.Add(new("player_profile_id", profile.ToString()!));
            if (player.HasValue) claims.Add(new("player_id", player.ToString()!));
            if (credential.HasValue) claims.Add(new("player_device_id", credential.ToString()!));
            if (display.HasValue) claims.Add(new("device_id", display.ToString()!));
            var scheme = CookieAuthenticationDefaults.AuthenticationScheme;
            var options = Host.App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
            Cookies[options.Cookie.Name!] = options.TicketDataFormat.Protect(new AuthenticationTicket(new(new ClaimsIdentity(claims, scheme)), new AuthenticationProperties(), scheme));
        }
        public string Browser => SecureAccessService.Hash(Cookies[SecureAccess.BrowserCookie]);
        public SecureEnrollment Pending => Host.Access.ForBrowser(Browser)!;
        public async Task<string> PhysicalCode()
        {
            using var screen = Host.Client(physical: true);
            var response = await screen.Send("/secure-access/physical");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            return Regex.Match(html, $"data-request=\"{Pending.Id}\"[\\s\\S]*?data-physical-code=\"([^\"]+)\"").Groups[1].Value;
        }
        public async Task Bootstrap(bool recovery = true)
        {
            Assert.Equal(HttpStatusCode.Redirect, (await Post("BeginDm", ("recovery", recovery.ToString()))).StatusCode);
            var code = await PhysicalCode(); Assert.Equal(16, code.Length);
            Assert.DoesNotContain(code, await (await Send("/secure-access")).Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Redirect, (await Post("Redeem", ("Code", code))).StatusCode);
        }
        public void Dispose() => Client.Dispose();
    }
    public static (Guid Profile, Guid Player, Guid Credential) AddPlayer(Host host)
    {
        var profile = new PlayerProfile { Name = "Fixture player", CreatedAtUtc = DateTime.UtcNow, RecoveryKeyHash = Guid.NewGuid().ToString() };
        var credential = new PlayerDeviceCredential { PlayerProfileId = profile.Id, TokenHash = Guid.NewGuid().ToString(), CreatedAtUtc = DateTime.UtcNow };
        var player = new PlayerConnection { PlayerProfileId = profile.Id, PlayerDeviceCredentialId = credential.Id, SessionId = host.App.Services.GetRequiredService<GameSessionService>().CurrentSession.SessionId, Name = "Fixture player" };
        using var db = host.Factory.CreateDbContext(); db.AddRange(profile, credential, player); db.SaveChanges();
        return (profile.Id, player.Id, credential.Id);
    }
    internal static async Task Enroll(Session member, Session dm)
    {
        Assert.Equal(HttpStatusCode.Redirect, (await member.Post("BeginMember")).StatusCode);
        var id = member.Pending.Id;
        Assert.Equal(HttpStatusCode.Redirect, (await dm.Post("Approve", ("id", id.ToString()))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await member.Post("Finish")).StatusCode);
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task PhysicalNetworkReadoutRejectsRemoteClientsAndHasNoMutationHandler()
    {
        using var host = new Host();
        using var remote = host.Client();
        using var physical = host.Client(physical: true, http: true);
        Assert.Equal(HttpStatusCode.NotFound, (await remote.Send("/setup/network-status")).StatusCode);
        var read = await physical.Send("/setup/network-status");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var html = await read.Content.ReadAsStringAsync();
        Assert.Contains("Read-only information on the physical hub", html);
        Assert.Contains("/setup", html);
        Assert.DoesNotContain("WifiPassword", html);
        Assert.DoesNotContain("Switch to private", html);
        var mutation = await physical.Send("/setup/network-status?handler=BeginHotspot", new FormUrlEncodedContent([]));
        Assert.False(mutation.IsSuccessStatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task PhysicalApprovalOverlayIsAvailableOnlyOnTrustedLocalHttps()
    {
        using var host = new Host();
        using var physical = host.Client(physical: true);
        using var legacy = host.Client(physical: true, http: true);
        using var remote = host.Client();
        var secure = await physical.Send("/setup");
        Assert.Equal(HttpStatusCode.OK, secure.StatusCode);
        var secureHtml = await secure.Content.ReadAsStringAsync();
        Assert.Contains("physicalApprovalOverlay", secureHtml);

        var http = await legacy.Send("/setup");
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        var httpHtml = await http.Content.ReadAsStringAsync();
        Assert.DoesNotContain("physicalApprovalOverlay", httpHtml);
        Assert.Equal(HttpStatusCode.NotFound, (await remote.Send("/setup")).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task OneTapPhysicalApproveDenyAndBrowserCompletionRespectHttpsCsrfAndIdentity()
    {
        using var host = new Host();
        using var requester = host.Client();
        using var physical = host.Client(physical: true);
        using var remote = host.Client();
        await remote.Token(); // Independent secure browser.
        Assert.Equal(HttpStatusCode.Redirect, (await requester.Post("BeginDm", ("recovery", "true"))).StatusCode);
        var pending = requester.Pending;
        var screen = await physical.Send("/secure-access/physical");
        Assert.Equal(HttpStatusCode.OK, screen.StatusCode);
        var markup = await screen.Content.ReadAsStringAsync();
        Assert.Contains("Approve browser", markup);
        Assert.Contains("Deny request", markup);
        Assert.Contains(pending.Reference, markup);
        Assert.Contains("revokes all previous secure approvals", markup);
        var token = await physical.Token("/secure-access/physical");
        // A physical HTTPS request without the Razor antiforgery token cannot
        // make a decision, even when the exact request id is known.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await physical.Send("/secure-access/physical?handler=Approve",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["id"] = pending.Id.ToString()
                }))).StatusCode);
        var approval = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = pending.Id.ToString(),
            ["__RequestVerificationToken"] = token
        });
        Assert.Equal(HttpStatusCode.Forbidden,
            (await remote.Send("/secure-access/physical?handler=Approve", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["id"] = pending.Id.ToString(), ["__RequestVerificationToken"] = token
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,
            (await physical.Send("/secure-access/physical?handler=Approve", approval)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await physical.Send("/secure-access/physical?handler=Approve",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["id"] = pending.Id.ToString(), ["__RequestVerificationToken"] = token
                }))).StatusCode);
        var requesterHtml = await (await requester.Send("/secure-access")).Content.ReadAsStringAsync();
        Assert.Contains("data-physical-approved=\"true\"", requesterHtml);
        Assert.Equal(HttpStatusCode.Forbidden, (await remote.Post("FinishPhysical")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await requester.Post("FinishPhysical")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await requester.Send("/media/fixture")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await requester.Post("FinishPhysical")).StatusCode);

        Assert.Equal(HttpStatusCode.Redirect, (await requester.Post("BeginDm")).StatusCode);
        var denied = requester.Pending.Id;
        var denyToken = await physical.Token("/secure-access/physical");
        Assert.Equal(HttpStatusCode.Redirect,
            (await physical.Send("/secure-access/physical?handler=Deny",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["id"] = denied.ToString(), ["__RequestVerificationToken"] = denyToken
                }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await requester.Post("FinishPhysical")).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task RememberedDmRestoresAcrossWeeklyRestartAndForgetSurvivesAnotherRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "secure-remember-dm-" + Guid.NewGuid());
        var clock = new Clock();
        string browserCookie;
        Guid rememberedId;
        using (var first = new Host(directory, clock))
        {
            using var dm = first.Client(); await dm.Bootstrap();
            var result = await dm.Post("Remember", ("label", "DM tablet"));
            Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
            var refreshed = result.Headers.GetValues("Set-Cookie").Single(x =>
                x.StartsWith(SecureAccess.BrowserCookie + "=", StringComparison.Ordinal));
            Assert.Contains("max-age=2592000", refreshed, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", refreshed, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", refreshed, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=strict", refreshed, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("domain=", refreshed, StringComparison.OrdinalIgnoreCase);
            browserCookie = dm.Cookies[SecureAccess.BrowserCookie];
            var trust = first.Access.Remembered(first.Access.Grants().Single()).Single();
            Assert.Equal("DM tablet", trust.Label);
            Assert.Equal("DM", trust.Identity.Role);
            Assert.True(trust.Expires <= clock.GetUtcNow().AddDays(30));
            rememberedId = trust.Id;
        }
        clock.Advance(TimeSpan.FromDays(8));
        using (var second = new Host(directory, clock))
        {
            using var returned = second.Client();
            returned.Cookies[SecureAccess.BrowserCookie] = browserCookie;
            Assert.Equal(HttpStatusCode.Forbidden, (await returned.Send("/media/fixture")).StatusCode);
            var response = await returned.Send("/secure-access");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await returned.Send("/secure-access")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await returned.Send("/media/fixture")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await returned.Post("Remember", ("label", "Extend without proof"))).StatusCode);
            var page = await (await returned.Send("/secure-access")).Content.ReadAsStringAsync();
            Assert.Contains("DM tablet", page);
            Assert.Contains("Forget browser", page);
            Assert.Equal(HttpStatusCode.Redirect, (await returned.Post("ForgetRemembered",
                ("id", rememberedId.ToString()))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await returned.Send("/media/fixture")).StatusCode);
        }
        using (var third = new Host(directory, clock))
        {
            using var returned = third.Client();
            returned.Cookies[SecureAccess.BrowserCookie] = browserCookie;
            Assert.Equal(HttpStatusCode.OK, (await returned.Send("/secure-access")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await returned.Send("/media/fixture")).StatusCode);
        }
        Directory.Delete(directory, true);
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task RememberedBrowserIsOptInAndDeniedOnHttpOrAfterAbsoluteExpiry()
    {
        using var host = new Host();
        using var dm = host.Client(); await dm.Bootstrap();
        var browserCookie = dm.Cookies[SecureAccess.BrowserCookie];
        host.Clock.Advance(TimeSpan.FromHours(13));
        using var notRemembered = host.Client();
        notRemembered.Cookies[SecureAccess.BrowserCookie] = browserCookie;
        Assert.Equal(HttpStatusCode.OK, (await notRemembered.Send("/secure-access")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await notRemembered.Send("/media/fixture")).StatusCode);

        // A separate fresh physical authorization is required to opt in.
        using var fresh = host.Client(); await fresh.Bootstrap();
        Assert.Equal(HttpStatusCode.Redirect, (await fresh.Post("Remember", ("label", "Table DM"))).StatusCode);
        var trusted = fresh.Cookies[SecureAccess.BrowserCookie];
        using var http = host.Client(http: true);
        http.Cookies[SecureAccess.BrowserCookie] = trusted;
        Assert.Equal((HttpStatusCode)426, (await http.Send("/secure-access")).StatusCode);
        host.Clock.Advance(TimeSpan.FromDays(31));
        using var expired = host.Client();
        expired.Cookies[SecureAccess.BrowserCookie] = trusted;
        Assert.Equal(HttpStatusCode.OK, (await expired.Send("/secure-access")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await expired.Send("/media/fixture")).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task ExplicitGrantRevocationAlsoRevokesRememberedTrust()
    {
        using var host = new Host();
        using var dm = host.Client(); await dm.Bootstrap();
        var owner = AddPlayer(host);
        using var player = host.Client();
        player.Legacy("Player", owner.Profile, owner.Player, owner.Credential);
        await Enroll(player, dm);
        Assert.Equal(HttpStatusCode.Redirect, (await player.Post("Remember", ("label", "Test handset"))).StatusCode);
        var browser = player.Cookies[SecureAccess.BrowserCookie];
        var grantId = host.Access.Grants().Single(g => g.Identity.Role == "Player").Id;
        Assert.Equal(HttpStatusCode.Redirect, (await dm.Post("Revoke", ("id", grantId.ToString()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Send("/media/fixture")).StatusCode);
        using var replay = host.Client();
        replay.Cookies[SecureAccess.BrowserCookie] = browser;
        Assert.Equal(HttpStatusCode.OK, (await replay.Send("/secure-access")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await replay.Send("/media/fixture")).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task RememberedPlayerCannotEscalateRolesAndCredentialChangeRevokesTrust()
    {
        using var host = new Host();
        using var dm = host.Client(); await dm.Bootstrap();
        var owner = AddPlayer(host);
        using var player = host.Client();
        player.Legacy("Player", owner.Profile, owner.Player, owner.Credential);
        await Enroll(player, dm);
        Assert.Equal(HttpStatusCode.Redirect, (await player.Post("Remember", ("label", "Player phone"))).StatusCode);
        var browser = player.Cookies[SecureAccess.BrowserCookie];
        host.Clock.Advance(TimeSpan.FromDays(8));
        using var otherRole = host.Client();
        otherRole.Cookies[SecureAccess.BrowserCookie] = browser;
        otherRole.Legacy("DM");
        Assert.Equal(HttpStatusCode.OK, (await otherRole.Send("/secure-access")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherRole.Send("/media/fixture")).StatusCode);

        using var restored = host.Client();
        restored.Cookies[SecureAccess.BrowserCookie] = browser;
        Assert.Equal(HttpStatusCode.Redirect, (await restored.Send("/secure-access")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await restored.Send("/secure-access")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await restored.Send("/media/fixture")).StatusCode);
        using (var db = host.Factory.CreateDbContext())
        {
            db.PlayerDeviceCredentials.Single(c => c.Id == owner.Credential).TokenHash = "rotated-credential";
            db.SaveChanges();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await restored.Send("/media/fixture")).StatusCode);
        using var replay = host.Client();
        replay.Cookies[SecureAccess.BrowserCookie] = browser;
        Assert.Equal(HttpStatusCode.OK, (await replay.Send("/secure-access")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await replay.Send("/media/fixture")).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task RememberedDisplayLosesPrivateAccessWhenDeviceIsUntrusted()
    {
        using var host = new Host();
        using var dm = host.Client(); await dm.Bootstrap();
        var id = Guid.NewGuid();
        using (var db = host.Factory.CreateDbContext())
        {
            db.RegisteredDevices.Add(new RegisteredDevice {
                Id = id, Name = "Weekly table screen", DeviceType = "Display",
                IsTrusted = true, PairedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }
        using var display = host.Client(); display.Legacy("Display", display: id);
        await Enroll(display, dm);
        Assert.Equal(HttpStatusCode.Redirect, (await display.Post("Remember", ("label", "Table screen"))).StatusCode);
        var browser = display.Cookies[SecureAccess.BrowserCookie];
        host.Clock.Advance(TimeSpan.FromDays(8));
        using var returned = host.Client();
        returned.Cookies[SecureAccess.BrowserCookie] = browser;
        Assert.Equal(HttpStatusCode.Redirect, (await returned.Send("/secure-access")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await returned.Send("/media/fixture")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await returned.Send("/appearance/fixture", new FormUrlEncodedContent([]))).StatusCode);
        using (var db = host.Factory.CreateDbContext())
        {
            db.RegisteredDevices.Single(d => d.Id == id).IsTrusted = false;
            db.SaveChanges();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await returned.Send("/media/fixture")).StatusCode);
        using var afterRevocation = host.Client();
        afterRevocation.Cookies[SecureAccess.BrowserCookie] = browser;
        Assert.Equal(HttpStatusCode.OK, (await afterRevocation.Send("/secure-access")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await afterRevocation.Send("/media/fixture")).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task PhysicalOneTapApprovesPlayerAndDisplayWithoutIssuingGrantsToOtherBrowsers()
    {
        using var host = new Host();
        using var dm = host.Client(); await dm.Bootstrap();
        using var physical = host.Client(physical: true);
        var owner = AddPlayer(host);
        using var player = host.Client();
        player.Legacy("Player", owner.Profile, owner.Player, owner.Credential);
        Assert.Equal(HttpStatusCode.Redirect, (await player.Post("BeginMember")).StatusCode);
        var pending = player.Pending;
        var screen = await physical.Send("/secure-access/physical");
        Assert.Equal(HttpStatusCode.OK, screen.StatusCode);
        var html = await screen.Content.ReadAsStringAsync();
        Assert.Contains("Player authorization", html);
        Assert.Contains(pending.Reference, html);
        Assert.Contains(owner.Profile.ToString(), html);
        Assert.DoesNotContain("data-physical-code=", html);
        var token = await physical.Token("/secure-access/physical");
        Assert.Equal(HttpStatusCode.Redirect, (await physical.Send("/secure-access/physical?handler=Approve",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["id"] = pending.Id.ToString(), ["__RequestVerificationToken"] = token
            }))).StatusCode);
        Assert.True(player.Pending.PhysicalApproved);
        using var other = host.Client();
        other.Legacy("Player", owner.Profile, owner.Player, owner.Credential);
        await other.Token();
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Post("Finish")).StatusCode);
        var waitingHtml = await (await player.Send("/secure-access")).Content.ReadAsStringAsync();
        Assert.Contains("id=\"finishMemberApproval\"", waitingHtml);
        Assert.Contains("data-physical-approved=\"true\"", waitingHtml);
        Assert.Equal(HttpStatusCode.Redirect, (await player.Post("Finish")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await player.Send("/media/fixture")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Post("Finish")).StatusCode);

        // Denial consumes this exact pending request; a stale browser cannot finish.
        Assert.Equal(HttpStatusCode.Redirect, (await player.Post("BeginMember")).StatusCode);
        var denied = player.Pending.Id;
        token = await physical.Token("/secure-access/physical");
        Assert.Equal(HttpStatusCode.Redirect, (await physical.Send("/secure-access/physical?handler=Deny",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["id"] = denied.ToString(), ["__RequestVerificationToken"] = token
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Post("Finish")).StatusCode);

        using (var db = host.Factory.CreateDbContext())
        {
            db.RegisteredDevices.Add(new RegisteredDevice {
                Id = Guid.Parse("29dc33a4-5b1d-4c68-8cc9-8a1edfa4329e"),
                Name = "Table display", DeviceType = "Display", IsTrusted = true,
                PairedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }
        using var display = host.Client();
        display.Legacy("Display", display: Guid.Parse("29dc33a4-5b1d-4c68-8cc9-8a1edfa4329e"));
        Assert.Equal(HttpStatusCode.Redirect, (await display.Post("BeginMember")).StatusCode);
        var displayPending = display.Pending;
        html = await (await physical.Send("/secure-access/physical")).Content.ReadAsStringAsync();
        Assert.Contains("Display authorization", html);
        Assert.Contains(displayPending.Identity.Display.ToString(), html);
        token = await physical.Token("/secure-access/physical");
        Assert.Equal(HttpStatusCode.Redirect, (await physical.Send("/secure-access/physical?handler=Approve",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["id"] = displayPending.Id.ToString(), ["__RequestVerificationToken"] = token
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await display.Post("Finish")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await display.Send("/media/fixture")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await display.Send("/appearance/fixture", new FormUrlEncodedContent([]))).StatusCode);
        host.DeleteOnDispose();
    }


    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task QuickConnectDisplayCanPairWithDmBeforePhysicalSecureEnrollment()
    {
        using var host = new Host();
        using var dm = host.Client(); await dm.Bootstrap();
        using var display = host.Client();
        var token = host.App.Services.GetRequiredService<GameSessionService>().CurrentSession.DisplayToken;
        using var invalid = host.Client();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await invalid.Send("/display/connect?token=invalid")).StatusCode);
        var invitation = await display.Send("/display/connect?token=" + Uri.EscapeDataString(token));
        Assert.Equal(HttpStatusCode.Redirect, invitation.StatusCode);
        Assert.Contains("/display/pair", invitation.Headers.Location?.ToString() ?? "",
            StringComparison.OrdinalIgnoreCase);
        // QR invitation must never create an unpaired Display authority cookie.
        Assert.Equal(HttpStatusCode.Redirect, (await display.Send("/display")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await display.Post("BeginMember")).StatusCode);
        var pairing = await display.Send("/display/pair");
        Assert.Equal(HttpStatusCode.OK, pairing.StatusCode);
        Assert.Contains("Pair Display", await pairing.Content.ReadAsStringAsync());
        Guid pairingId;
        using (var db = host.Factory.CreateDbContext())
            pairingId = db.DevicePairingRequests.Single().Id;
        host.App.Services.GetRequiredService<DeviceRegistryService>().ApprovePairing(pairingId);
        var status = await display.Send("/display/pair?handler=Status");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Contains("approved", await status.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await display.Post("BeginMember")).StatusCode);
        using var physical = host.Client(physical: true);
        var verification = await physical.Token("/secure-access/physical");
        Assert.Equal(HttpStatusCode.Redirect,
            (await physical.Send("/secure-access/physical?handler=Approve",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["id"] = display.Pending.Id.ToString(),
                    ["__RequestVerificationToken"] = verification
                }))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await display.Post("Finish")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await display.Send("/media/fixture")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await display.Send("/appearance/fixture", new FormUrlEncodedContent([]))).StatusCode);
        host.DeleteOnDispose();
    }


    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task NormalTrustedDmLoginShowsWaitingAndPhysicalApprovalWithoutManualSecureAccess()
    {
        using var host = new Host();
        using var dm = host.Client();
        using var physical = host.Client(physical: true);
        var pin = host.App.Services.GetRequiredService<GameSessionService>().CurrentSession.DmPin;
        var loginToken = await dm.Token("/dm/login");
        var login = await dm.Send("/dm/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Pin"] = pin, ["__RequestVerificationToken"] = loginToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Contains("/secure-access/wait", login.Headers.Location?.ToString() ?? "");

        // GET renders an antiforgery form but must not create a request or grant.
        var waiting = await dm.Send("/secure-access/wait?start=true");
        Assert.Equal(HttpStatusCode.OK, waiting.StatusCode);
        var markup = await waiting.Content.ReadAsStringAsync();
        Assert.Contains("Preparing approval", markup);
        Assert.Contains("data-auto-submit=\"true\"", markup);
        Assert.Null(host.Access.ForBrowser(dm.Browser));

        var token = await dm.Token("/secure-access/wait?start=true");
        var noCsrf = await dm.Send("/secure-access/wait?handler=Begin",
            new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        Assert.Null(host.Access.ForBrowser(dm.Browser));

        var begin = await dm.Send("/secure-access/wait?handler=Begin",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, begin.StatusCode);
        var pending = dm.Pending;
        Assert.Equal("dm-recover", pending.Purpose);
        var pendingHtml = await (await dm.Send("/secure-access/wait")).Content.ReadAsStringAsync();
        Assert.Contains("Waiting for approval on hub", pendingHtml);
        Assert.Contains(pending.Reference, pendingHtml);

        var physicalToken = await physical.Token("/secure-access/physical");
        Assert.Equal(HttpStatusCode.Redirect, (await physical.Send("/secure-access/physical?handler=Approve",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["id"] = pending.Id.ToString(), ["__RequestVerificationToken"] = physicalToken
            }))).StatusCode);
        var finished = await dm.Send("/secure-access/wait?handler=Finish",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["__RequestVerificationToken"] = await dm.Token("/secure-access/wait")
            }));
        Assert.Equal(HttpStatusCode.Redirect, finished.StatusCode);
        Assert.Equal("/dm", finished.Headers.Location?.ToString());
        Assert.Null(host.Access.ForBrowser(dm.Browser));
        Assert.Equal(HttpStatusCode.OK, (await dm.Send("/media/fixture")).StatusCode);
        host.DeleteOnDispose();
    }


    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task WaitingRememberPreferenceRequiresCsrfExactBrowserAndApproval()
    {
        using var host = new Host();
        using var physical = host.Client(physical: true);
        using var dm = host.Client();
        using var other = host.Client();
        dm.Legacy("DM");
        other.Legacy("DM");
        var token = await dm.Token("/secure-access/wait?start=true");
        Assert.Equal(HttpStatusCode.Redirect, (await dm.Send("/secure-access/wait?handler=Begin",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["__RequestVerificationToken"] = token
            }))).StatusCode);
        var pending = dm.Pending;
        Assert.False(pending.RememberRequested);
        Assert.Equal(HttpStatusCode.BadRequest, (await dm.Send("/secure-access/wait?handler=RememberPreference",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["id"] = pending.Id.ToString(), ["remember"] = "true"
            }))).StatusCode);
        Assert.False(dm.Pending.RememberRequested);

        await other.Token("/secure-access/wait");
        var otherToken = await other.Token("/secure-access/wait");
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Send("/secure-access/wait?handler=RememberPreference",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["id"] = pending.Id.ToString(), ["remember"] = "true",
                ["__RequestVerificationToken"] = otherToken
            }))).StatusCode);
        Assert.False(dm.Pending.RememberRequested);

        var formToken = await dm.Token("/secure-access/wait");
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/secure-access/wait?handler=RememberPreference",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["id"] = Guid.NewGuid().ToString(), ["remember"] = "true",
                ["__RequestVerificationToken"] = formToken
            }))).StatusCode);
        Assert.False(dm.Pending.RememberRequested);

        Assert.Equal(HttpStatusCode.Redirect, (await dm.Send("/secure-access/wait?handler=RememberPreference",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["id"] = pending.Id.ToString(), ["remember"] = "true",
                ["__RequestVerificationToken"] = formToken
            }))).StatusCode);
        Assert.True(dm.Pending.RememberRequested);
        var waiting = await (await dm.Send("/secure-access/wait")).Content.ReadAsStringAsync();
        Assert.Contains("Remember this browser for 30 days", waiting);
        Assert.Contains("checked=\"checked\"", waiting);

        var physicalToken = await physical.Token("/secure-access/physical");
        Assert.Equal(HttpStatusCode.Redirect, (await physical.Send("/secure-access/physical?handler=Approve",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["id"] = pending.Id.ToString(), ["__RequestVerificationToken"] = physicalToken
            }))).StatusCode);
        var finishToken = await dm.Token("/secure-access/wait");
        Assert.Equal(HttpStatusCode.Redirect, (await dm.Send("/secure-access/wait?handler=Finish",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["__RequestVerificationToken"] = finishToken
            }))).StatusCode);
        var grant = host.Access.Grants().Single(g => g.Browser == dm.Browser);
        Assert.Single(host.Access.Remembered(grant));
        Assert.Equal("DM browser", host.Access.Remembered(grant)[0].Label);
        var devicesPage = await dm.Send("/secure-access/devices");
        Assert.Equal(HttpStatusCode.OK, devicesPage.StatusCode);
        Assert.Contains("DM browser", await devicesPage.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Send("/secure-access/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/secure-access/wait?handler=RememberPreference",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["id"] = pending.Id.ToString(), ["remember"] = "true",
                ["__RequestVerificationToken"] = formToken
            }))).StatusCode);

        // Missing opt-in means no persistent trust for this other browser.
        Assert.Equal(HttpStatusCode.Redirect, (await other.Send("/secure-access/wait?handler=Begin",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["__RequestVerificationToken"] = await other.Token("/secure-access/wait")
            }))).StatusCode);
        var noConsent = other.Pending;
        var token2 = await physical.Token("/secure-access/physical");
        Assert.Equal(HttpStatusCode.Redirect, (await physical.Send("/secure-access/physical?handler=Approve",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["id"] = noConsent.Id.ToString(), ["__RequestVerificationToken"] = token2
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await other.Send("/secure-access/wait?handler=Finish",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["__RequestVerificationToken"] = await other.Token("/secure-access/wait")
            }))).StatusCode);
        var otherGrant = host.Access.Grants().Single(g => g.Browser == other.Browser);
        Assert.Empty(host.Access.Remembered(otherGrant).Where(b => b.Browser == other.Browser));
        var forgetToken = await dm.Token("/secure-access/devices");
        Assert.Equal(HttpStatusCode.Redirect, (await dm.Send("/secure-access/devices?handler=Forget",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["id"] = host.Access.Remembered(grant).Single().Id.ToString(),
                ["__RequestVerificationToken"] = forgetToken
            }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/media/fixture")).StatusCode);
        Assert.DoesNotContain(host.Access.Grants(), g => g.Browser == dm.Browser);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task PlayerWaitingDenialDoesNotAutomaticallyStartAnotherRequest()
    {
        using var host = new Host();
        using var dm = host.Client(); await dm.Bootstrap();
        using var physical = host.Client(physical: true);
        var playerId = AddPlayer(host);
        using var player = host.Client();
        player.Legacy("Player", playerId.Profile, playerId.Player, playerId.Credential);
        Assert.Equal(HttpStatusCode.OK, (await player.Send("/secure-access/wait?start=true")).StatusCode);
        await player.Token("/secure-access/wait?start=true");
        Assert.Null(host.Access.ForBrowser(player.Browser));

        var token = await player.Token("/secure-access/wait");
        Assert.Equal(HttpStatusCode.Redirect, (await player.Send("/secure-access/wait?handler=Begin",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["__RequestVerificationToken"] = token
            }))).StatusCode);
        var pending = player.Pending;
        Assert.Equal("enroll-player", pending.Purpose);

        var physicalToken = await physical.Token("/secure-access/physical");
        Assert.Equal(HttpStatusCode.Redirect, (await physical.Send("/secure-access/physical?handler=Deny",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["id"] = pending.Id.ToString(), ["__RequestVerificationToken"] = physicalToken
            }))).StatusCode);
        var denied = await (await player.Send("/secure-access/wait")).Content.ReadAsStringAsync();
        Assert.Contains("Approval not completed", denied);
        Assert.Contains("href=\"/join\">Return to player join</a>", denied);
        Assert.Null(host.Access.ForBrowser(player.Browser));
        Assert.Null(host.Access.ForBrowser(player.Browser)); // Poll/reload is read-only.
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Send("/media/fixture")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Send("/secure-access/wait?handler=Finish",
            new FormUrlEncodedContent(new Dictionary<string, string> {
                ["__RequestVerificationToken"] = await player.Token("/secure-access/wait")
            }))).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task PhysicalTapConsentIsBrowserBoundExpiringAndSingleUse()
    {
        using var host = new Host();
        using var requester = host.Client();
        using var stranger = host.Client();
        await stranger.Token(); // Populate an independent secure browser binding.
        Assert.Equal(HttpStatusCode.Redirect, (await requester.Post("BeginDm", ("recovery", "true"))).StatusCode);
        var id = requester.Pending.Id;
        Assert.Throws<SecureAccessDenied>(() => host.Access.FinishPhysicalDm(requester.Browser, Origin));
        host.Access.ApprovePhysicalDm(id);
        Assert.Throws<SecureAccessDenied>(() => host.Access.ApprovePhysicalDm(id));
        Assert.Throws<SecureAccessDenied>(() => host.Access.FinishPhysicalDm(stranger.Browser, Origin));
        Assert.Throws<SecureAccessDenied>(() => host.Access.FinishPhysicalDm(requester.Browser, "https://wrong.example"));
        var grant = host.Access.FinishPhysicalDm(requester.Browser, Origin);
        Assert.Equal("DM", grant.Identity.Role);
        Assert.NotNull(host.Access.Validate(grant.Id, grant.Generation, grant.Epoch, requester.Browser, Origin));
        Assert.Throws<SecureAccessDenied>(() => host.Access.FinishPhysicalDm(requester.Browser, Origin));

        Assert.Equal(HttpStatusCode.Redirect, (await requester.Post("BeginDm")).StatusCode);
        var denied = requester.Pending.Id;
        host.Access.DenyPhysicalDm(denied);
        Assert.Throws<SecureAccessDenied>(() => host.Access.ApprovePhysicalDm(denied));
        Assert.Throws<SecureAccessDenied>(() => host.Access.FinishPhysicalDm(requester.Browser, Origin));

        Assert.Equal(HttpStatusCode.Redirect, (await requester.Post("BeginDm")).StatusCode);
        var expired = requester.Pending.Id;
        host.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Throws<SecureAccessDenied>(() => host.Access.ApprovePhysicalDm(expired));
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task LegacyReplayFailsAndFreshPhysicalDmProofProtectsRealEndpoints()
    {
        using var host = new Host();
        using var dm = host.Client(); dm.Legacy("DM");
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/media/fixture")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/secure-access/physical")).StatusCode);
        dm.Client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/secure-access/physical")).StatusCode);
        using var http = host.Client(physical: true, http: true); http.Legacy("DM");
        Assert.Equal((HttpStatusCode)426, (await http.Send("/secure-access/physical")).StatusCode);
        await dm.Bootstrap();
        Assert.Equal(HttpStatusCode.OK, (await dm.Send("/media/fixture")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dm.Send("/appearance/fixture", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/dm/network?handler=Provision", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/api/coordination/enroll", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.Send("/system/health")).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task ProofBindsExactBrowserPurposeOriginSessionAndIsSingleUseWithSecureCookies()
    {
        using var host = new Host(); using var a = host.Client(); using var b = host.Client();
        await a.Post("BeginDm", ("recovery", "true")); var code = await a.PhysicalCode();
        await b.Post("BeginDm", ("recovery", "true"));
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Post("Redeem", ("Code", code))).StatusCode);
        var token = await a.Token();
        var response = await a.PostToken("Redeem", token, ("Code", code));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(SecureAccess.Cookie + "="));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase); Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase); Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.Post("Redeem", ("Code", code))).StatusCode);
        using var thief = host.Client(); thief.Legacy("DM");
        thief.Cookies[SecureAccess.Cookie] = a.Cookies[SecureAccess.Cookie];
        Assert.Equal(HttpStatusCode.Forbidden, (await thief.Send("/media/fixture")).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task CsrfAndOriginFailuresDoNotConsumeCodeAndExpiryAttemptsAreBounded()
    {
        using var host = new Host(); using var dm = host.Client();
        await dm.Post("BeginDm", ("recovery", "true")); var code = await dm.PhysicalCode();
        var noCsrf = await dm.Send("/secure-access?handler=Redeem", new FormUrlEncodedContent(new Dictionary<string, string> { ["Code"] = code }));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        var token = await dm.Token();
        var noOrigin = await dm.Send("/secure-access?handler=Redeem", new FormUrlEncodedContent(new Dictionary<string, string> { ["Code"] = code, ["__RequestVerificationToken"] = token }), origin: false);
        Assert.Equal(HttpStatusCode.Forbidden, noOrigin.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await dm.Post("Redeem", ("Code", code))).StatusCode);
        await dm.Post("BeginDm"); var expired = await dm.PhysicalCode(); host.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Post("Redeem", ("Code", expired))).StatusCode);
        await dm.Post("BeginDm"); var bounded = await dm.PhysicalCode();
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Forbidden, (await dm.Post("Redeem", ("Code", "0000000000000000"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Post("Redeem", ("Code", bounded))).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task PlayerAndDisplayRequireExplicitApprovalAndCannotMixOrEscalateRoles()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var owner = AddPlayer(host); using var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Send("/media/fixture")).StatusCode);
        await player.Post("BeginMember");
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Post("Finish")).StatusCode);
        await dm.Post("Approve", ("id", player.Pending.Id.ToString())); await player.Post("Finish");
        Assert.Equal(HttpStatusCode.OK, (await player.Send("/media/fixture")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Post("Approve", ("id", Guid.NewGuid().ToString()))).StatusCode);
        var other = AddPlayer(host); player.Legacy("Player", other.Profile, other.Player, other.Credential);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Send("/media/fixture")).StatusCode);
        player.Legacy("DM"); Assert.Equal(HttpStatusCode.Forbidden, (await player.Send("/media/fixture")).StatusCode);
        using var db = host.Factory.CreateDbContext(); var display = new RegisteredDevice { Name = "Display", PairedAtUtc = DateTime.UtcNow }; db.Add(display); db.SaveChanges();
        using var screen = host.Client(); screen.Legacy("Display", display: display.Id); await Enroll(screen, dm);
        Assert.Equal(HttpStatusCode.OK, (await screen.Send("/media/fixture")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await screen.Send("/appearance/fixture", new FormUrlEncodedContent([]))).StatusCode);
        screen.Legacy("Player", owner.Profile, owner.Player, owner.Credential); Assert.Equal(HttpStatusCode.Forbidden, (await screen.Send("/media/fixture")).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task ConcurrentRedemptionApprovalCancellationAndRateBoundsFailClosed()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Post("BeginDm", ("recovery", "true"));
        var code = await dm.PhysicalCode(); var token = await dm.Token();
        // Independent clients share the exact request browser, as simultaneous tabs would.
        using var tab = host.Client(); foreach (var item in dm.Cookies) tab.Cookies[item.Key] = item.Value;
        var responses = await Task.WhenAll(dm.PostToken("Redeem", token, ("Code", code)), tab.PostToken("Redeem", token, ("Code", code)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Redirect);
        if (responses[0].StatusCode != HttpStatusCode.Redirect) foreach (var item in tab.Cookies) dm.Cookies[item.Key] = item.Value;
        var owner = AddPlayer(host); using var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential);
        await player.Post("BeginMember"); var id = player.Pending.Id;
        await Task.WhenAll(dm.Post("Approve", ("id", id.ToString())), player.Post("Cancel"));
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Post("Finish")).StatusCode);
        for (var i = 0; i < 12; i++) await player.Post("BeginMember");
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Post("BeginMember")).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task SessionsSurviveRestartAndRevocationDoesNotReviveAfterRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "secure-restart-" + Guid.NewGuid());
        Dictionary<string, string> cookies;
        Guid grantId;
        using (var first = new Host(directory))
        {
            using var dm = first.Client(); await dm.Bootstrap();
            grantId = first.Access.Grants().Single().Id; cookies = new(dm.Cookies);
        }
        using (var second = new Host(directory))
        {
            using var dm = second.Client(); foreach (var cookie in cookies) dm.Cookies[cookie.Key] = cookie.Value;
            Assert.Equal(HttpStatusCode.OK, (await dm.Send("/media/fixture")).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await dm.Post("Revoke", ("id", grantId.ToString()))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/media/fixture")).StatusCode);
        }
        using (var third = new Host(directory))
        {
            using var replay = third.Client(); foreach (var cookie in cookies) replay.Cookies[cookie.Key] = cookie.Value;
            Assert.Equal(HttpStatusCode.Forbidden, (await replay.Send("/media/fixture")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await replay.Post("Revoke", ("id", grantId.ToString()))).StatusCode);
        }
        Directory.Delete(directory, true);
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task CorruptMissingInterruptedStateAndPhysicalRecoveryNeverUseHttpAuthority()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var path = Path.Combine(host.DirectoryPath, "secure-access", "state.bin");
        var old = new Dictionary<string, string>(dm.Cookies);
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/media/fixture")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dm.Send("/system/health")).StatusCode);
        await dm.Bootstrap(recovery: true);
        using var stale = host.Client(); foreach (var item in old) stale.Cookies[item.Key] = item.Value;
        Assert.Equal(HttpStatusCode.Forbidden, (await stale.Send("/media/fixture")).StatusCode);
        File.WriteAllText(Path.Combine(host.DirectoryPath, "secure-access", "mutation.pending"), "interrupted");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(Path.Combine(host.DirectoryPath, "secure-access", "mutation.pending"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/media/fixture")).StatusCode);
        await dm.Bootstrap(recovery: true);
        File.Delete(path); Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/media/fixture")).StatusCode);
        await dm.Bootstrap(recovery: true);
        Assert.Equal(HttpStatusCode.OK, (await dm.Send("/media/fixture")).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task LegacyIdentityReassociationRecoveryAndRevocationNeverFollowOldGrants()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var owner = AddPlayer(host); using var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential); await Enroll(player, dm);
        string originalHash; using (var db = host.Factory.CreateDbContext()) originalHash = db.PlayerDeviceCredentials.Single(c => c.Id == owner.Credential).TokenHash;
        using (var db = host.Factory.CreateDbContext()) { db.PlayerDeviceCredentials.Single(c => c.Id == owner.Credential).TokenHash = "replacement"; db.SaveChanges(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Send("/media/fixture")).StatusCode);
        using (var db = host.Factory.CreateDbContext()) { db.PlayerDeviceCredentials.Single(c => c.Id == owner.Credential).TokenHash = originalHash; db.SaveChanges(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Send("/media/fixture")).StatusCode);
        using var legacyDm = host.Client(); legacyDm.Legacy("DM");
        var token = await legacyDm.Token("/dm/players");
        var reset = await legacyDm.Send("/dm/players?handler=ResetRecoveryKey", new FormUrlEncodedContent(new Dictionary<string, string> { ["profileId"] = owner.Profile.ToString(), ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Forbidden, reset.StatusCode);
        host.DeleteOnDispose();
    }
    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task ActualHttpLoginAndSetupPinNeverBecomeSecureAuthority()
    {
        using var host = new Host(); using var http = host.Client(http: true);
        var token = await http.Token("/dm/login");
        var pin = host.App.Services.GetRequiredService<GameSessionService>().CurrentSession.DmPin;
        var login = await http.Send("/dm/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Pin"] = pin, ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        using var replay = host.Client(); foreach (var cookie in http.Cookies) replay.Cookies[cookie.Key] = cookie.Value;
        Assert.Equal(HttpStatusCode.Forbidden, (await replay.Send("/media/fixture")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await replay.Post("BeginDm", ("recovery", "true"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await replay.Post("Redeem", ("Code", pin))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await replay.Send("/appearance/fixture", new FormUrlEncodedContent([]))).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task ApprovalCannotMoveToAnotherBrowserOrReassociatedIdentity()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var owner = AddPlayer(host); using var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential);
        await player.Post("BeginMember"); var pending = player.Pending;
        await dm.Post("Approve", ("id", pending.Id.ToString()));
        using var otherBrowser = host.Client(); otherBrowser.Legacy("Player", owner.Profile, owner.Player, owner.Credential);
        await otherBrowser.Token();
        Assert.Equal(HttpStatusCode.Forbidden, (await otherBrowser.Post("Finish", ("id", pending.Id.ToString()))).StatusCode);
        using (var db = host.Factory.CreateDbContext()) { db.PlayerProfiles.Single(p => p.Id == owner.Profile).RecoveryKeyHash = "replaced-recovery"; db.SaveChanges(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Post("Finish")).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task RevokedApproverCannotFinishPendingApprovalAndCancellationWinsRedemptionRace()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var owner = AddPlayer(host); using var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential);
        await player.Post("BeginMember"); await dm.Post("Approve", ("id", player.Pending.Id.ToString()));
        var dmId = host.Access.Grants().Single(g => g.Identity.Role == "DM").Id;
        await dm.Post("Revoke", ("id", dmId.ToString()));
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Post("Finish")).StatusCode);
        await dm.Bootstrap();
        await player.Post("BeginMember"); await dm.Post("Approve", ("id", player.Pending.Id.ToString()));
        var token = await player.Token(); using var tab = host.Client(); foreach (var cookie in player.Cookies) tab.Cookies[cookie.Key] = cookie.Value;
        await Task.WhenAll(player.PostToken("Finish", token), tab.PostToken("Cancel", token));
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Send("/media/fixture")).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task FailedPersistenceLeavesDurableFenceAndCannotAcknowledgeRevocationOrIssueAuthority()
    {
        var directory = Path.Combine(Path.GetTempPath(), "secure-write-failure-" + Guid.NewGuid());
        Dictionary<string, string> cookies;
        using (var first = new Host(directory))
        {
            using var dm = first.Client(); await dm.Bootstrap();
            var id = first.Access.Grants().Single().Id;
            cookies = new(dm.Cookies); first.Failure.Enabled = true;
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await dm.Post("Revoke", ("id", id.ToString()))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/media/fixture")).StatusCode);
        }
        using (var next = new Host(directory))
        {
            using var replay = next.Client(); foreach (var cookie in cookies) replay.Cookies[cookie.Key] = cookie.Value;
            Assert.Equal(HttpStatusCode.Forbidden, (await replay.Send("/media/fixture")).StatusCode);
            await replay.Bootstrap();
            Assert.Equal(HttpStatusCode.OK, (await replay.Send("/media/fixture")).StatusCode);
        }
        Directory.Delete(directory, true);
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task ChallengeQueueIsBoundedAndRestartInvalidatesPendingProof()
    {
        var directory = Path.Combine(Path.GetTempPath(), "secure-pending-restart-" + Guid.NewGuid());
        Dictionary<string, string> oldCookies; string oldCode;
        using (var first = new Host(directory))
        {
            using var request = first.Client(); await request.Post("BeginDm", ("recovery", "true")); oldCode = await request.PhysicalCode(); oldCookies = new(request.Cookies);
            for (var i = 0; i < 31; i++)
            {
                using var browser = first.Client(); browser.Client.DefaultRequestHeaders.Remove("Fixture-Peer"); browser.Client.DefaultRequestHeaders.Add("Fixture-Peer", "192.168.4." + (i + 30));
                Assert.Equal(HttpStatusCode.Redirect, (await browser.Post("BeginDm", ("recovery", "true"))).StatusCode);
            }
            using var excess = first.Client(); excess.Client.DefaultRequestHeaders.Remove("Fixture-Peer"); excess.Client.DefaultRequestHeaders.Add("Fixture-Peer", "192.168.4.90");
            Assert.Equal(HttpStatusCode.Forbidden, (await excess.Post("BeginDm", ("recovery", "true"))).StatusCode);
            Assert.Equal(32, first.Access.PhysicalRequests().Count);
            using var owner = first.Client(physical: true);
            Assert.Equal(HttpStatusCode.Redirect, (await owner.Post("BeginDm", ("recovery", "true"))).StatusCode);
            Assert.Equal(32, first.Access.PhysicalRequests().Count);
        }
        using (var restarted = new Host(directory))
        {
            using var request = restarted.Client(); foreach (var cookie in oldCookies) request.Cookies[cookie.Key] = cookie.Value;
            Assert.Equal(HttpStatusCode.Forbidden, (await request.Post("Redeem", ("Code", oldCode))).StatusCode);
            Assert.False(restarted.Access.Initialized);
        }
        Directory.Delete(directory, true);
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task LegacyRecoveryDeviceDeletionPinAndSessionChangesCannotRestoreSecureGrants()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var owner = AddPlayer(host); using var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential); await Enroll(player, dm);
        var token = await dm.Token("/dm/players");
        Assert.Equal(HttpStatusCode.Redirect, (await dm.Send("/dm/players?handler=RevokeAllDevices", new FormUrlEncodedContent(new Dictionary<string, string> { ["profileId"] = owner.Profile.ToString(), ["__RequestVerificationToken"] = token }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Send("/media/fixture")).StatusCode);
        using (var db = host.Factory.CreateDbContext()) { db.GameSessions.Single().DmPin = "replaced-pin"; db.SaveChanges(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/media/fixture")).StatusCode);
        await dm.Bootstrap();
        using (var db = host.Factory.CreateDbContext()) { db.GameSessions.Single().SessionId = Guid.NewGuid(); db.SaveChanges(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/media/fixture")).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task LegacyPlayerMutationCannotRevokeAnotherIdentityAndLogoutCannotBeUndoneIntoAuthority()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var a = AddPlayer(host); var b = AddPlayer(host);
        using var first = host.Client(); first.Legacy("Player", a.Profile, a.Player, a.Credential); await Enroll(first, dm);
        using var second = host.Client(); second.Legacy("Player", b.Profile, b.Player, b.Credential); await Enroll(second, dm);
        var token = await first.Token("/player");
        await first.Send("/player/?handler=RevokeDevice", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = token, ["credentialId"] = Guid.NewGuid().ToString(), ["PROFILEID"] = b.Profile.ToString() }));
        Assert.Equal(HttpStatusCode.OK, (await second.Send("/media/fixture")).StatusCode);
        using var oldHttp = host.Client(http: true); oldHttp.Legacy("Player", b.Profile, b.Player, b.Credential);
        token = await oldHttp.Token("/player");
        await oldHttp.Send("/player/?handler=SignOut", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        using (var db = host.Factory.CreateDbContext()) { db.PlayerConnections.Single(p => p.Id == b.Player).IsActive = true; db.SaveChanges(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await second.Send("/media/fixture")).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxTheory("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    [InlineData("/dm/players", "ResetRecoveryKey")]
    [InlineData("/dm/players", "RevokeAllDevices")]
    [InlineData("/dm/players", "DeleteProfile")]
    [InlineData("/dm/devices", "Approve")]
    [InlineData("/dm/devices", "Forget")]
    [InlineData("/dm/devices", "RemovePlayer")]
    [InlineData("/dm/profiles", "DeleteProfile")]
    [InlineData("/dm/profiles", "DeleteCharacter")]
    public async Task LegacyAdminRoutesCannotUseCapturedCookieAsSensitiveAuthority(string page, string handler)
    {
        using var host = new Host(); using var legacy = host.Client(); legacy.Legacy("DM");
        var token = await legacy.Token(page);
        Assert.Equal(HttpStatusCode.Forbidden, (await legacy.Send(page + "/?handler=" + handler,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["profileId"] = Guid.NewGuid().ToString(), ["id"] = Guid.NewGuid().ToString() }))).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task DisplayReplacementAndDeletionInvalidateApprovedIdentityDurably()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        Guid deviceId; DateTime paired;
        using (var db = host.Factory.CreateDbContext()) { var device = new RegisteredDevice { Name = "Original", PairedAtUtc = DateTime.UtcNow }; db.Add(device); db.SaveChanges(); deviceId = device.Id; paired = device.PairedAtUtc; }
        using var display = host.Client(); display.Legacy("Display", display: deviceId); await Enroll(display, dm);
        var token = await dm.Token("/dm/devices");
        Assert.Equal(HttpStatusCode.Redirect, (await dm.Send("/dm/devices?handler=Forget", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = token, ["id"] = deviceId.ToString() }))).StatusCode);
        using (var db = host.Factory.CreateDbContext()) { db.RegisteredDevices.Single(d => d.Id == deviceId).IsTrusted = true; db.SaveChanges(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await display.Send("/media/fixture")).StatusCode);
        using (var db = host.Factory.CreateDbContext()) { db.RegisteredDevices.Remove(db.RegisteredDevices.Single(d => d.Id == deviceId)); db.SaveChanges(); db.Add(new RegisteredDevice { Id = deviceId, Name = "Replacement", PairedAtUtc = paired }); db.SaveChanges(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await display.Send("/media/fixture")).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task SecureExpiryAndFailedIssuanceCannotRenewThroughLegacyAuthority()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        host.Clock.Advance(TimeSpan.FromHours(12) + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/media/fixture")).StatusCode);
        Assert.DoesNotContain("Authorized as DM", await (await dm.Send("/secure-access")).Content.ReadAsStringAsync());
        using var fresh = host.Client();
        await fresh.Post("BeginDm", ("recovery", "true")); var code = await fresh.PhysicalCode();
        host.Failure.Enabled = true;
        var failed = await fresh.Post("Redeem", ("Code", code));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.False(fresh.Cookies.ContainsKey(SecureAccess.Cookie));
        Assert.Null(host.Access.ForBrowser(fresh.Browser));
        host.Failure.Enabled = false;
        Assert.Equal(HttpStatusCode.Forbidden, (await fresh.Post("Redeem", ("Code", code))).StatusCode);
        await fresh.Bootstrap();
        Assert.Equal(HttpStatusCode.OK, (await fresh.Send("/media/fixture")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/media/fixture")).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task SensitiveIdentityChangesRequireBothCsrfAndExactOriginBeforeInvalidation()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var owner = AddPlayer(host); using var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential); await Enroll(player, dm);
        string? original; using (var db = host.Factory.CreateDbContext()) original = db.PlayerProfiles.Single(p => p.Id == owner.Profile).RecoveryKeyHash;
        var token = await dm.Token("/dm/players");
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/dm/players?handler=ResetRecoveryKey", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["profileId"] = owner.Profile.ToString(), ["__RequestVerificationToken"] = token }), origin: false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await dm.Send("/dm/players?handler=ResetRecoveryKey", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["profileId"] = owner.Profile.ToString() }))).StatusCode);
        using (var db = host.Factory.CreateDbContext()) Assert.Equal(original, db.PlayerProfiles.Single(p => p.Id == owner.Profile).RecoveryKeyHash);
        Assert.Equal(HttpStatusCode.OK, (await player.Send("/media/fixture")).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task ActualCharacterEditorGetRequiresFreshAuthorityAtItsRoutedUrl()
    {
        using var host = new Host(); var owner = AddPlayer(host); Guid id;
        using (var db = host.Factory.CreateDbContext())
        {
            var character = new PlayerCharacter { PlayerProfileId = owner.Profile, Name = "Editor fixture", MaxHp = 20, CurrentHp = 18, CreatedAtUtc = DateTime.UtcNow };
            db.Add(character); db.SaveChanges(); id = character.Id;
        }
        var page = $"/player/character/{id:D}/edit";
        using var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Send(page)).StatusCode);
        using var http = host.Client(http: true); http.Legacy("Player", owner.Profile, owner.Player, owner.Credential);
        Assert.Equal((HttpStatusCode)426, (await http.Send(page)).StatusCode);
        using var dm = host.Client(); await dm.Bootstrap(); await Enroll(player, dm);
        Assert.Equal(HttpStatusCode.OK, (await player.Send(page)).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxTheory("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    [InlineData(SecurePersistenceSync.FenceCreated)]
    [InlineData(SecurePersistenceSync.StateReplaced)]
    [InlineData(SecurePersistenceSync.FenceRemoved)]
    public async Task DirectorySyncFailureNeverAcknowledgesRevocationAndRestartRequiresPhysicalRecovery(SecurePersistenceSync stage)
    {
        var directory = Path.Combine(Path.GetTempPath(), "secure-sync-failure-" + Guid.NewGuid());
        Dictionary<string, string> cookies;
        using (var first = new Host(directory))
        {
            using var dm = first.Client(); await dm.Bootstrap();
            var id = first.Access.Grants().Single().Id; cookies = new(dm.Cookies);
            first.Failure.SyncStage = stage;
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await dm.Post("Revoke", ("id", id.ToString()))).StatusCode);
            Assert.True(File.Exists(Path.Combine(directory, "secure-access", "mutation.pending")));
            Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/media/fixture")).StatusCode);
        }
        using (var restarted = new Host(directory))
        {
            using var replay = restarted.Client(); foreach (var cookie in cookies) replay.Cookies[cookie.Key] = cookie.Value;
            Assert.Equal(HttpStatusCode.Forbidden, (await replay.Send("/media/fixture")).StatusCode);
            await replay.Bootstrap();
            Assert.Equal(HttpStatusCode.OK, (await replay.Send("/media/fixture")).StatusCode);
        }
        Directory.Delete(directory, true);
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task ActualUpdaterRollbackFenceRejectsRestoredRevokedTicketAndPreservesGameplayData()
    {
        var directory = Path.Combine(Path.GetTempPath(), "secure-updater-rollback-" + Guid.NewGuid());
        Dictionary<string, string> staleCookies;
        using (var first = new Host(directory))
        {
            using var dm = first.Client(); await dm.Bootstrap();
            var owner = AddPlayer(first);
            Guid displayId;
            using (var db = first.Factory.CreateDbContext()) { var device = new RegisteredDevice { Name = "Preserved display pairing", PairedAtUtc = DateTime.UtcNow }; db.Add(device); db.SaveChanges(); displayId = device.Id; }
            using var display = first.Client(); display.Legacy("Display", display: displayId);
            Assert.Equal(HttpStatusCode.OK, (await display.Send("/display")).StatusCode);
            var state = Path.Combine(directory, "secure-access", "state.bin");
            var backup = File.ReadAllBytes(state); staleCookies = new(dm.Cookies);
            var id = first.Access.Grants().Single().Id;
            await dm.Post("Revoke", ("id", id.ToString()));
            Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/media/fixture")).StatusCode);
            File.WriteAllBytes(state, backup); // Deliberately model updater whole-data restore.
            var keys = Directory.GetFiles(Path.Combine(directory, "DataProtection-Keys")).ToDictionary(p => p, File.ReadAllBytes);
            var media = Path.Combine(directory, "Media"); Directory.CreateDirectory(media); File.WriteAllBytes(Path.Combine(media, "portrait.png"), [1, 2, 3]);
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
            var source = File.ReadAllText(Path.Combine(root, "hub-updater/runtime/initiativetracker-install-update"));
            var python = source.Split("# BEGIN SECURE ROLLBACK FENCE\n")[1].Split("# END SECURE ROLLBACK FENCE")[0];
            var info = new System.Diagnostics.ProcessStartInfo("/usr/bin/python3") { RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true };
            info.ArgumentList.Add("-I"); info.ArgumentList.Add("-"); info.ArgumentList.Add(directory);
            using var process = System.Diagnostics.Process.Start(info)!;
            await process.StandardInput.WriteAsync(python); process.StandardInput.Close();
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
            Assert.True(File.Exists(Path.Combine(directory, "secure-access", "mutation.pending")));
            Assert.Equal(backup, File.ReadAllBytes(state));
            foreach (var key in keys) Assert.Equal(key.Value, File.ReadAllBytes(key.Key));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(media, "portrait.png")));
            using (var db = first.Factory.CreateDbContext()) Assert.True(db.PlayerProfiles.Any(p => p.Id == owner.Profile));
            Assert.Equal(HttpStatusCode.OK, (await display.Send("/display")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/media/fixture")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await dm.Send("/system/health")).StatusCode);
        }
        using (var restarted = new Host(directory))
        {
            using var replay = restarted.Client(); foreach (var cookie in staleCookies) replay.Cookies[cookie.Key] = cookie.Value;
            Assert.Equal(HttpStatusCode.Forbidden, (await replay.Send("/media/fixture")).StatusCode);
            await replay.Bootstrap();
            Assert.Equal(HttpStatusCode.OK, (await replay.Send("/media/fixture")).StatusCode);
        }
        Directory.Delete(directory, true);
    }

}
