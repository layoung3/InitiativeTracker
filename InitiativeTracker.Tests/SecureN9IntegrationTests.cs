using System.Net;
using System.Net.Http.Headers;
using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Services;
using InitiativeTracker.Web.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using static InitiativeTracker.Tests.SecureAccessWebTests;

namespace InitiativeTracker.Tests;

public sealed class SecureN9IntegrationTests
{
    private static Guid Character(Host host, Guid profile)
    {
        using var db = host.Factory.CreateDbContext();
        var character = new PlayerCharacter { PlayerProfileId = profile, Name = "Integration hero", MaxHp = 20, CurrentHp = 18, CreatedAtUtc = DateTime.UtcNow };
        db.Add(character); db.SaveChanges(); return character.Id;
    }
    private static string Editor(Guid id, string scope = "character") => $"/appearance/{scope}/{id:D}";
    private static async Task<HttpResponseMessage> Upload(Session player, Guid id)
    {
        var token = await player.Token(Editor(id));
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent("portrait"), "kind");
        var file = new ByteArrayContent(N9MediaTests.Raster()); file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "Upload", "fixture.png");
        return await player.Send(Editor(id) + "?handler=Upload", content);
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task ActualN9BrandingAndPlayerImagesRequireFreshAuthorizationAndKeepOwnership()
    {
        using var host = new Host(); using var dm = host.Client(); dm.Legacy("DM");
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send(Editor(Guid.Empty, "application"))).StatusCode);
        await dm.Bootstrap();
        var authorityPage=await dm.Send("/secure-access");
        Assert.Equal("same-origin",authorityPage.Headers.GetValues("Referrer-Policy").Single());
        var token = await dm.Token(Editor(Guid.Empty, "application"));
        var saved = await dm.Send(Editor(Guid.Empty, "application") + "?handler=Settings", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = token, ["ApplicationName"] = "Secure integration", ["ShowApplicationLogo"] = "true" }));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal("Secure integration", host.App.Services.GetRequiredService<BrandingService>().ApplicationName);
        var owner = AddPlayer(host); var id = Character(host, owner.Profile);
        using var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Send(Editor(id))).StatusCode);
        await Enroll(player, dm);
        Assert.Equal(HttpStatusCode.Redirect, (await Upload(player, id)).StatusCode);
        var media = await player.Send(MediaPaths.CharacterPortrait(id));
        Assert.Equal(HttpStatusCode.OK, media.StatusCode);
        Assert.Equal("image/png", media.Content.Headers.ContentType?.MediaType);
        var other = AddPlayer(host); using var stranger = host.Client(); stranger.Legacy("Player", other.Profile, other.Player, other.Credential); await Enroll(stranger, dm);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Send(Editor(id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Send(MediaPaths.CharacterPortrait(id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await player.Send(Editor(Guid.Empty, "application"))).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxTheory("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    [InlineData("null")]
    [InlineData("https://other.owner-domain.org:5445")]
    public async Task NullAndForeignOriginsRemainDeniedWithBrowserCompatibleReferrerPolicy(string origin)
    {
        using var host=new Host(); using var dm=host.Client(); await dm.Bootstrap();
        var token=await dm.Token();
        dm.Client.DefaultRequestHeaders.Add("Origin",origin);
        using var proof=await dm.Send("/secure-access?handler=BeginDm",new FormUrlEncodedContent(new Dictionary<string,string>
            {["__RequestVerificationToken"]=token,["recovery"]="true"}),origin:false);
        Assert.Equal(HttpStatusCode.Forbidden,proof.StatusCode);
        using var settings=await dm.Send(Editor(Guid.Empty,"application")+"?handler=Settings",new FormUrlEncodedContent(new Dictionary<string,string>
            {["__RequestVerificationToken"]=token,["ApplicationName"]="Rejected foreign form"}),origin:false);
        Assert.Equal(HttpStatusCode.Forbidden,settings.StatusCode);
        Assert.NotEqual("Rejected foreign form",host.App.Services.GetRequiredService<BrandingService>().ApplicationName);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task ActualN9DisplayKeepsPublicProjectionAndLegacyVisibilityCannotPublishArtwork()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var owner = AddPlayer(host); var id = Character(host, owner.Profile);
        using var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential); await Enroll(player, dm); await Upload(player, id);
        Guid displayId;
        using (var db = host.Factory.CreateDbContext()) { var device = new RegisteredDevice { Name = "Integration display", PairedAtUtc = DateTime.UtcNow }; db.Add(device); db.SaveChanges(); displayId = device.Id; }
        using var display = host.Client(); display.Legacy("Display", display: displayId); await Enroll(display, dm);
        Assert.Equal(HttpStatusCode.NotFound, (await display.Send(MediaPaths.CharacterPortrait(id))).StatusCode);
        var state = host.App.Services.GetRequiredService<EncounterStateService>(); state.AddPlayerCharacter(id);
        var combatant = state.CurrentEncounter.Combatants.Single(); state.SetCombatantVisibility(combatant.Id, true, true);
        using var legacy = host.Client(); legacy.Legacy("DM");
        var token = await legacy.Token("/dm");
        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["combatantId"] = combatant.Id.ToString(), ["isHiddenFromPublic"] = "false" };
        Assert.Equal(HttpStatusCode.Forbidden, (await legacy.Send("/dm/?handler=SetVisibility", new FormUrlEncodedContent(form))).StatusCode);
        Assert.True(state.CurrentEncounter.Combatants.Single().IsHiddenFromPublic);
        token = await dm.Token("/dm"); form["__RequestVerificationToken"] = token;
        Assert.Equal(HttpStatusCode.Redirect, (await dm.Send("/dm?handler=SetVisibility", new FormUrlEncodedContent(form))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await display.Send(MediaPaths.CharacterPortrait(id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await display.Send(Editor(id))).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task ActualN9AssetOwnerCannotFollowLegacyCharacterReassociation()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var owner = AddPlayer(host); var next = AddPlayer(host); var id = Character(host, owner.Profile);
        using var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential); await Enroll(player, dm); await Upload(player, id);
        using var other = host.Client(); other.Legacy("Player", next.Profile, next.Player, next.Credential); await Enroll(other, dm);
        using (var db = host.Factory.CreateDbContext()) { db.PlayerCharacters.Single(c => c.Id == id).PlayerProfileId = next.Profile; db.SaveChanges(); }
        Assert.Equal(HttpStatusCode.NotFound, (await other.Send(MediaPaths.CharacterPortrait(id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await dm.Send(Editor(id))).StatusCode);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task ActualRecoveryCreatesNoSecureAuthorityAndRequiresNewBrowserApproval()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var owner = AddPlayer(host); using var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential); await Enroll(player, dm);
        var registry = host.App.Services.GetRequiredService<PlayerRegistryService>(); var recovery = registry.RegenerateRecoveryKey(owner.Profile);
        var game = host.App.Services.GetRequiredService<GameSessionService>().CurrentSession;
        var token = await player.Token("/join");
        var response = await player.Send("/join", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = token, ["JoinCode"] = game.JoinCode, ["JoinMode"] = "recover", ["RecoveryKey"] = recovery }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Send("/media/fixture")).StatusCode);
        await Enroll(player, dm);
        Assert.Equal(HttpStatusCode.OK, (await player.Send("/media/fixture")).StatusCode);
        host.DeleteOnDispose();
    }
}
