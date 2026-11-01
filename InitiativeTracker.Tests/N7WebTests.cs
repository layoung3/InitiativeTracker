using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InitiativeTracker.Tests;

public sealed class N7WebTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "n7-tests-" + Guid.NewGuid());
    private readonly WebApplicationFactory<EncounterStateService> app;
    private readonly HttpClient client;
    private EncounterStateService State => app.Services.GetRequiredService<EncounterStateService>();
    private IDbContextFactory<InitiativeTrackerDbContext> Factory => app.Services.GetRequiredService<IDbContextFactory<InitiativeTrackerDbContext>>();

    public N7WebTests()
    {
        app = new WebApplicationFactory<EncounterStateService>().WithWebHostBuilder(builder =>
            builder.UseSetting("Tracker:DataDirectory", directory).UseEnvironment("Development"));
        client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private Guid Character(Guid owner)
    {
        using var db = Factory.CreateDbContext();
        db.PlayerProfiles.Add(new PlayerProfile { Id = owner, Name = "Test player" });
        var character = new PlayerCharacter { PlayerProfileId = owner, Name = "Test character", MaxHp = 20, CurrentHp = 17, TemporaryHp = 4, ExhaustionLevel = 3 };
        db.PlayerCharacters.Add(character);
        db.SaveChanges();
        return character.Id;
    }

    private void SignIn(string role, Guid? owner = null, bool revoked = false, bool staleSession = false)
    {
        var session = app.Services.GetRequiredService<GameSessionService>().CurrentSession.SessionId;
        var claims = new List<Claim> { new(ClaimTypes.Role, role), new("session_id", (staleSession ? Guid.NewGuid() : session).ToString()) };
        if (owner.HasValue)
        {
            using var db = Factory.CreateDbContext();
            var credential = new PlayerDeviceCredential { PlayerProfileId = owner.Value, TokenHash = Guid.NewGuid().ToString(), RevokedAtUtc = revoked ? DateTime.UtcNow : null };
            var player = new PlayerConnection { PlayerProfileId = owner.Value, SessionId = session, PlayerDeviceCredentialId = credential.Id, Name = "Test player" };
            db.PlayerDeviceCredentials.Add(credential); db.PlayerConnections.Add(player); db.SaveChanges();
            claims.AddRange([new("player_id", player.Id.ToString()), new("player_profile_id", owner.Value.ToString()), new("player_device_id", credential.Id.ToString())]);
        }
        if (role == "Display")
        {
            var registry = app.Services.GetRequiredService<DeviceRegistryService>();
            var device = registry.ApprovePairing(registry.CreatePairingRequest().Id);
            claims.Add(new("device_id", device.Id.ToString()));
        }
        var scheme = CookieAuthenticationDefaults.AuthenticationScheme;
        var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, scheme)), new AuthenticationProperties(), scheme);
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie", options.Cookie.Name + "=" + options.TicketDataFormat.Protect(ticket));
    }

    private async Task<string> Token(string page)
    {
        var response = await client.GetAsync(page);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        return WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    }

    private Task<HttpResponseMessage> Post(string page, string token, params (string Key, string Value)[] values) =>
        client.PostAsync(page, new FormUrlEncodedContent(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("__RequestVerificationToken", token))));

    [Fact]
    public async Task InitialDisplayIncludesInitiativeExhaustionConditionsAndPublicProjection()
    {
        var id = Character(Guid.NewGuid());
        State.AddPlayerCharacter(id);
        var combatant = Assert.Single(State.CurrentEncounter.Combatants);
        State.AddCombatantCondition(combatant.Id, "Poisoned", false);
        State.AddCombatants("Secret fixture NPC", -20, 44, 55, 1, true, true);
        State.AddCombatants("Visible private HP fixture", -10, 33, 66, 1, false, true);
        State.StartCombat();
        SignIn("Display");
        var response = await client.GetAsync("/display");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("/css/satellite-display", html);
        Assert.Contains("data-satellite-readability=\"true\"", html);
        Assert.Contains("combatant-initiative", html);
        Assert.Contains("Current turn. Initiative", html);
        Assert.Contains("aria-current=\"true\"", html);
        Assert.Contains("combatant-name-heading", html);
        Assert.Contains("Exhaustion 3", html);
        Assert.Contains("condition-poisoned", html);
        Assert.DoesNotContain("Secret fixture NPC", html);
        var rows = Regex.Matches(html, "data-combatant-id=\"([^\"]+)\"");
        Assert.Equal(2, rows.Count);
        // Only the player-visible HP block is emitted; NPC HP remains projected out.
        Assert.Single(Regex.Matches(html, "class=\"combatant-hp\""));
        Assert.Single(Regex.Matches(html, "class=\"combatant-temp-hp\""));
        SignIn("DM");
        var hubHtml = await (await client.GetAsync("/display")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("/css/satellite-display", hubHtml);
        Assert.Contains("data-satellite-readability=\"false\"", hubHtml);
        Assert.DoesNotContain("combatant-initiative", hubHtml);
    }

    [Fact]
    public void InitialVisibilityAndUndoPreservePublicFiltering()
    {
        State.AddCombatants("Secret", 15, 9, 12, 2, true, true);
        Assert.Empty(State.GetDisplayState().Combatants);
        Assert.All(State.CurrentEncounter.Combatants, c => Assert.True(c.IsHiddenFromPublic && c.IsHpHiddenFromPublic));
        var id = State.CurrentEncounter.Combatants[0].Id;
        State.SetCombatantVisibility(id, false, true);
        Assert.Null(Assert.Single(State.GetDisplayState().Combatants).CurrentHp);
        State.Undo();
        Assert.Empty(State.GetDisplayState().Combatants);
    }

    [Fact]
    public void OwnedSubmissionIsAtomicWithDuplicatesAndCombatPhase()
    {
        var owner = Guid.NewGuid(); var id = Character(owner);
        Assert.Throws<InvalidOperationException>(() => State.AddPlayerCharacter(id, submittingPlayerProfileId: Guid.NewGuid()));
        Parallel.For(0, 8, _ => { try { State.AddPlayerCharacter(id, submittingPlayerProfileId: owner); } catch (InvalidOperationException) { } });
        var combatant = Assert.Single(State.CurrentEncounter.Combatants);
        Assert.Equal(3, combatant.ExhaustionLevel);
        Assert.Equal(4, combatant.TemporaryHp);
        State.StartCombat();
        var otherOwner = Guid.NewGuid(); var other = Character(otherOwner);
        Assert.Throws<InvalidOperationException>(() => State.AddPlayerCharacter(other, submittingPlayerProfileId: otherOwner));
        Assert.Single(State.CurrentEncounter.Combatants);
        Assert.Throws<InvalidOperationException>(() => State.AddPlayerCharacter(other)); // Preserve existing DM setup-phase rule.
        Assert.Single(State.CurrentEncounter.Combatants);
    }

    [Fact]
    public void ExhaustionSurvivesPersistenceUndoAndPlayerVisibilitySemantics()
    {
        var owner = Guid.NewGuid(); var id = Character(owner);
        State.AddPlayerCharacter(id, true, true);
        Assert.Equal(17, Assert.Single(State.GetDisplayState().Combatants).CurrentHp);
        State.SetCharacterExhaustion(id, 6);
        Assert.Equal(6, Assert.Single(State.GetDisplayState().Combatants).ExhaustionLevel);
        State.Undo();
        Assert.Equal(3, Assert.Single(State.GetDisplayState().Combatants).ExhaustionLevel);
        var restored = new EncounterStateService(Factory, app.Services.GetRequiredService<CharacterService>());
        Assert.Equal(3, Assert.Single(restored.GetDisplayState().Combatants).ExhaustionLevel);
        Assert.Equal(3, app.Services.GetRequiredService<CharacterService>().GetCharacter(id)!.ExhaustionLevel);
    }

    [Fact]
    public void LegacySnapshotsHydrateExhaustionWithoutErasingCharacterState()
    {
        var owner = Guid.NewGuid(); var id = Character(owner);
        State.AddPlayerCharacter(id);
        State.SetCombatantVisibility(State.CurrentEncounter.Combatants[0].Id, true, true);
        using (var db = Factory.CreateDbContext())
        {
            var persisted = db.EncounterStates.Single();
            persisted.EncounterJson = Regex.Replace(persisted.EncounterJson, "\"exhaustionLevel\":3,?", "");
            persisted.UndoHistoryJson = Regex.Replace(persisted.UndoHistoryJson, "\"exhaustionLevel\":3,?", "");
            db.SaveChanges();
        }
        var restored = new EncounterStateService(Factory, app.Services.GetRequiredService<CharacterService>());
        Assert.Equal(3, Assert.Single(restored.CurrentEncounter.Combatants).ExhaustionLevel);
        restored.Undo();
        Assert.Equal(3, Assert.Single(restored.CurrentEncounter.Combatants).ExhaustionLevel);
        Assert.Equal(3, app.Services.GetRequiredService<CharacterService>().GetCharacter(id)!.ExhaustionLevel);
    }

    [Fact]
    public async Task PlayerHandlerChecksOwnerDuplicateAndSetupPhase()
    {
        var owner = Guid.NewGuid(); var id = Character(owner); var other = Character(Guid.NewGuid());
        SignIn("Player", owner);
        var token = await Token("/player");
        var url = "/player?handler=AddCharacter";
        await Post(url, token, ("characterId", other.ToString()));
        Assert.Empty(State.CurrentEncounter.Combatants);
        await Post(url, token, ("characterId", id.ToString()));
        await Post(url, token, ("characterId", id.ToString()));
        Assert.Single(State.CurrentEncounter.Combatants);
        State.ResetEncounter(); State.AddCombatants("DM", 1, null, null, 1); State.StartCombat();
        await Post(url, token, ("characterId", id.ToString()));
        Assert.Single(State.CurrentEncounter.Combatants);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RevokedCredentialsAndStaleSessionsCannotSubmit(bool revoked, bool stale)
    {
        var owner = Guid.NewGuid(); var id = Character(owner);
        SignIn("Player", owner, revoked, stale);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/player")).StatusCode);
        var response = await Post("/player?handler=AddCharacter", "invalid", ("characterId", id.ToString()));
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(State.CurrentEncounter.Combatants);
    }

    [Fact]
    public async Task DmAutosaveRequiresDmAndAntiforgeryAndReturnsJson()
    {
        State.AddCombatants("Enemy", 1, 10, 10, 1);
        var id = State.CurrentEncounter.Combatants[0].Id;
        var url = "/dm?handler=SetVisibility";
        var anon = await Post(url, "", ("combatantId", id.ToString()), ("isHiddenFromPublic", "true"));
        Assert.NotEqual(HttpStatusCode.OK, anon.StatusCode);
        SignIn("DM");
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(url, "", ("combatantId", id.ToString()))).StatusCode);
        var token = await Token("/dm");
        client.DefaultRequestHeaders.Add("X-Visibility-Autosave", "1");
        var saved = await Post(url, token, ("combatantId", id.ToString()), ("isHiddenFromPublic", "true"));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Contains("\"saved\":true", await saved.Content.ReadAsStringAsync());
        Assert.Empty(State.GetDisplayState().Combatants);
        var owner = Guid.NewGuid(); Character(owner); SignIn("Player", owner);
        var denied = await Post(url, token, ("combatantId", id.ToString()));
        Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
        Assert.Empty(State.GetDisplayState().Combatants);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DmShowsCurrentExhaustionBesideNameAndReceivesPlayerSheetUpdates(bool started)
    {
        var owner = Guid.NewGuid(); var id = Character(owner);
        State.AddPlayerCharacter(id);
        State.AddCombatantCondition(State.CurrentEncounter.Combatants[0].Id, "Prone", false);
        if (started) State.StartCombat();
        SignIn("DM");
        var dmCookie = client.DefaultRequestHeaders.GetValues("Cookie").Single();
        var socketClient = app.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request => request.Headers["Cookie"] = dmCookie;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var socket = await socketClient.ConnectAsync(new Uri("ws://localhost/hubs/encounter"), timeout.Token);
        await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e")),
            WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[16384];
        var handshake = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        Assert.Contains("{}", Encoding.UTF8.GetString(buffer, 0, handshake.Count));

        SignIn("Player", owner);
        var playerCookie = client.DefaultRequestHeaders.GetValues("Cookie").Single();
        var sheet = $"/player/character/{id}";
        var token = await Token(sheet);
        foreach (var level in new[] { 3, 5, 0 })
        {
            client.DefaultRequestHeaders.Remove("Cookie"); client.DefaultRequestHeaders.Add("Cookie", playerCookie);
            Assert.Equal(HttpStatusCode.Redirect, (await Post(sheet + "?handler=SetExhaustion", token,
                ("exhaustionLevel", level.ToString()))).StatusCode);
            // Receive the actual broadcast over the authenticated DM SignalR socket.
            JsonElement? update = null;
            while (update is null)
            {
                var frame = new StringBuilder();
                WebSocketReceiveResult received;
                do
                {
                    received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                    Assert.NotEqual(WebSocketMessageType.Close, received.MessageType);
                    frame.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));
                } while (!received.EndOfMessage);
                foreach (var record in frame.ToString().Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
                {
                    using var json = JsonDocument.Parse(record);
                    if (json.RootElement.TryGetProperty("target", out var target) && target.GetString() == "EncounterUpdated")
                        update = json.RootElement.GetProperty("arguments")[0].Clone();
                }
            }
            var publicCharacter = update.Value.GetProperty("combatants")[0];
            Assert.Equal(level, publicCharacter.GetProperty("exhaustionLevel").GetInt32());
            Assert.Equal(17, publicCharacter.GetProperty("currentHp").GetInt32());
            Assert.Equal(4, publicCharacter.GetProperty("temporaryHp").GetInt32());
            Assert.Equal("Prone", publicCharacter.GetProperty("conditions")[0].GetProperty("name").GetString());
            client.DefaultRequestHeaders.Remove("Cookie"); client.DefaultRequestHeaders.Add("Cookie", dmCookie);
            var html = await client.GetStringAsync("/dm");
            var heading = Assert.Single(Regex.Matches(html, "class=\"dm-combatant-name-heading\"[^>]*>(.*?)</div>", RegexOptions.Singleline).Cast<Match>()).Groups[1].Value;
            Assert.Contains("Test character", heading);
            if (level == 0) Assert.DoesNotContain("dm-exhaustion-badge", html);
            else
            {
                Assert.Single(Regex.Matches(html, "dm-exhaustion-badge").Cast<Match>());
                Assert.Contains($"Exhaustion {level}</span>", heading);
            }
        }
    }

    [Fact]
    public async Task SetupRejectsNonLoopbackRequests()
    {
        // TestServer has no loopback remote address; no secret material should render.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/setup")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/setup/dm-qr")).StatusCode);
    }

    public void Dispose()
    {
        client.Dispose(); app.Dispose();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
