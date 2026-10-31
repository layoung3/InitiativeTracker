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
        public Host(string? directory = null, Clock? clock = null, bool endpoints = true)
        {
            DirectoryPath = directory ?? Path.Combine(Path.GetTempPath(), "secure-web-" + Guid.NewGuid());
            Clock = clock ?? new Clock();
            App = new WebApplicationFactory<EncounterStateService>().WithWebHostBuilder(builder => builder
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
                }));
        }
        public SecureAccessService Access => App.Services.GetRequiredService<SecureAccessService>();
        public IDbContextFactory<InitiativeTrackerDbContext> Factory => App.Services.GetRequiredService<IDbContextFactory<InitiativeTrackerDbContext>>();
        public Session Client(bool physical = false, bool http = false) => new(this, physical, http);
        public void Dispose() => App.Dispose();
        public void Delete() { if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true); }
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

    [Fact]
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
        host.Delete();
    }

    [Fact]
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
        host.Delete();
    }

    [Fact]
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
        host.Delete();
    }

    [Fact]
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
        host.Delete();
    }

    [Fact]
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
        host.Delete();
    }

    [Fact]
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

    [Fact]
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
        host.Delete();
    }

    [Fact]
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
        host.Delete();
    }
    [Fact]
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
        host.Delete();
    }

    [Fact]
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
        host.Delete();
    }

    [Fact]
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
        host.Delete();
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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
        host.Delete();
    }

    [Fact]
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
        host.Delete();
    }

    [Theory]
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
        host.Delete();
    }

    [Fact]
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
        host.Delete();
    }

    [Fact]
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
        host.Delete();
    }

    [Fact]
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
        host.Delete();
    }

    [Fact]
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
        host.Delete();
    }

    [Theory]
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

    [Fact]
    public async Task ActualUpdaterRollbackFenceRejectsRestoredRevokedTicketAndPreservesGameplayData()
    {
        var directory = Path.Combine(Path.GetTempPath(), "secure-updater-rollback-" + Guid.NewGuid());
        Dictionary<string, string> staleCookies;
        using (var first = new Host(directory))
        {
            using var dm = first.Client(); await dm.Bootstrap();
            var owner = AddPlayer(first);
            var state = Path.Combine(directory, "secure-access", "state.bin");
            var backup = File.ReadAllBytes(state); staleCookies = new(dm.Cookies);
            var id = first.Access.Grants().Single().Id;
            await dm.Post("Revoke", ("id", id.ToString()));
            Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send("/media/fixture")).StatusCode);
            File.WriteAllBytes(state, backup); // Deliberately model updater whole-data restore.
            var keys = Directory.GetFiles(Path.Combine(directory, "DataProtection-Keys")).ToDictionary(p => p, File.ReadAllBytes);
            var media = Path.Combine(directory, "media"); Directory.CreateDirectory(media); File.WriteAllBytes(Path.Combine(media, "portrait.png"), [1, 2, 3]);
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
