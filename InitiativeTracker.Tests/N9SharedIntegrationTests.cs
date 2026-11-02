using System.Net;
using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Infrastructure;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Headers;
using static InitiativeTracker.Tests.SecureAccessWebTests;

namespace InitiativeTracker.Tests;

public sealed class N9SharedIntegrationTests
{
    [LinuxTheory("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevocationDuringActualUploadCannotCommitAfterAwait(bool expire)
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var owner = AddPlayer(host);
        Guid id;
        using (var db = host.Factory.CreateDbContext())
        {
            var character = new PlayerCharacter { PlayerProfileId = owner.Profile, Name = "Awaited upload" };
            db.Add(character); db.SaveChanges(); id = character.Id;
        }
        using var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential); await Enroll(player, dm);
        var scheme = CookieAuthenticationDefaults.AuthenticationScheme;
        var options = host.App.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
        var context = new DefaultHttpContext { RequestServices = host.App.Services };
        context.User = options.TicketDataFormat.Unprotect(player.Cookies[options.Cookie.Name!])!.Principal;
        context.Request.Scheme = "https"; context.Request.Host = new HostString("hub.owner-domain.org:5445");
        context.Connection.LocalPort = 5445;
        context.Request.Path = $"/appearance/character/{id:D}";
        context.Request.Method = "POST"; context.Request.QueryString = new QueryString("?handler=Upload");
        context.Request.Headers.Origin = Origin;
        context.Request.Headers.Cookie = string.Join("; ", player.Cookies.Select(x => x.Key + "=" + x.Value));
        context.Request.RouteValues["scope"] = "character"; context.Request.RouteValues["subjectId"] = id;
        var accessor = host.App.Services.GetService<IHttpContextAccessor>();
        if (accessor is not null) accessor.HttpContext = context;
        using var input = new GatedStream(N9MediaTests.Raster());
        var page = ActivatorUtilities.CreateInstance<InitiativeTracker.Web.Pages.Appearance.IndexModel>(host.App.Services);
        page.PageContext = new PageContext { HttpContext = context };
        page.Upload = new FormFile(input, 0, input.Length, "Upload", "fixture.png");
        IActionResult? result = null;
        var upload = SecureAccess.Guard(context, async _ => result = await page.OnPostUploadAsync("character", id, "portrait", default));
        await input.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (expire) host.Clock.Advance(TimeSpan.FromHours(12));
        else host.Access.Cancel(player.Browser); // Durable secure-grant revocation; legacy identity stays active.
        input.Release.TrySetResult();
        await upload;
        Assert.IsType<NotFoundResult>(result);
        using var check = host.Factory.CreateDbContext();
        Assert.Empty(check.PresentationAssets);
        Assert.Empty(Directory.GetFiles(host.App.Services.GetRequiredService<MediaStore>().DirectoryPath));
        if (accessor is not null) accessor.HttpContext = null;
        host.DeleteOnDispose();
    }

    private sealed class DisabledOrigin : ITrustedBrowserOrigin
    {
        public bool Enabled => false;
        public string Origin => "https://unconfigured.example:5447";
        public bool TrustedRequest(HttpContext context) => context.Request.IsHttps;
        public bool Physical(HttpContext context) => false;
    }

    private static Guid Character(Host host, Guid profile)
    {
        using var db = host.Factory.CreateDbContext();
        var row = new PlayerCharacter { PlayerProfileId = profile, Name = "Integration hero", MaxHp = 20, CurrentHp = 18 };
        db.Add(row); db.SaveChanges(); return row.Id;
    }
    private static string Editor(Guid id, string scope = "character") => $"/appearance/{scope}/{id:D}";
    private static MultipartFormDataContent Body(string token, string kind = "portrait")
    {
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent(kind), "kind");
        var file = new ByteArrayContent(N9MediaTests.Raster()); file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "Upload", "fixture.png"); return content;
    }
    private static async Task Upload(Session member, Guid id, string scope = "character", string kind = "portrait")
    {
        using var body = Body(await member.Token(Editor(id, scope)), kind);
        Assert.Equal(HttpStatusCode.Redirect, (await member.Send(Editor(id, scope) + "?handler=Upload", body)).StatusCode);
    }

    [LinuxTheory("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledFoundationRejectsAlternateHttpsEvenWithLegacyOrSecureCookies(bool fresh)
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var owner = AddPlayer(host); var id = Character(host, owner.Profile);
        using var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential);
        await Enroll(player, dm); await Upload(player, id);
        var token = await player.Token(Editor(id));
        if (!fresh) { player.Cookies.Remove(SecureAccess.Cookie); dm.Cookies.Remove(SecureAccess.Cookie); }
        using var disabled = host.App.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITrustedBrowserOrigin>(); services.AddSingleton<ITrustedBrowserOrigin, DisabledOrigin>();
        }));
        using var client = disabled.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://unconfigured.example:5447"), AllowAutoRedirect = false, HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", player.Cookies.Select(x => x.Key + "=" + x.Value)));
        client.DefaultRequestHeaders.Add("Origin", "https://unconfigured.example:5447");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(MediaPaths.CharacterPortrait(id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Editor(id))).StatusCode);
        using var body = Body(token);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(Editor(id) + "?handler=Upload", body)).StatusCode);
        using var check = host.Factory.CreateDbContext(); Assert.Single(check.PresentationAssets);
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task BannersProfilesBrandingAndCsrfKeepTheirSeparatePermissions()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var owner = AddPlayer(host); var id = Character(host, owner.Profile);
        using var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential); await Enroll(player, dm);
        await Upload(player, id, kind: "banner"); await Upload(player, owner.Profile, "profile");
        await Upload(dm, Guid.Empty, "application", "banner"); await Upload(dm, Guid.Empty, "application", "logo");
        using var anonymous = host.Client();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.Send("/branding/logo")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await anonymous.Send(MediaPaths.CampaignBanner)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await player.Send(MediaPaths.CampaignBanner)).StatusCode);
        var strangerId = AddPlayer(host); using var stranger = host.Client();
        stranger.Legacy("Player", strangerId.Profile, strangerId.Player, strangerId.Credential); await Enroll(stranger, dm);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Send(MediaPaths.ProfilePortrait(owner.Profile))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Send(MediaPaths.CharacterBanner(id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await player.Send(Editor(Guid.Empty, "application"))).StatusCode);
        using var missingToken = Body("");
        Assert.Equal(HttpStatusCode.BadRequest, (await player.Send(Editor(id) + "?handler=Upload", missingToken)).StatusCode);
        using var missingOrigin = Body(await player.Token(Editor(id)));
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Send(Editor(id) + "?handler=Upload", missingOrigin, origin: false)).StatusCode);
        var before = host.App.Services.GetRequiredService<BrandingService>().ApplicationName;
        var token = await dm.Token(Editor(Guid.Empty, "application"));
        dm.Cookies.Remove(SecureAccess.Cookie);
        Assert.Equal(HttpStatusCode.Forbidden, (await dm.Send(Editor(Guid.Empty, "application") + "?handler=Settings",
            new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"] = token, ["ApplicationName"] = "Rejected branding" }))).StatusCode);
        Assert.Equal(before, host.App.Services.GetRequiredService<BrandingService>().ApplicationName);
        host.DeleteOnDispose();
    }

    [LinuxTheory("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestartPreservesMediaAndDurableSecureRevocation(bool revoke)
    {
        var host = new Host(); var directory = host.DirectoryPath; var clock = host.Clock;
        using var dm = host.Client(); await dm.Bootstrap();
        var owner = AddPlayer(host); var id = Character(host, owner.Profile);
        using var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential); await Enroll(player, dm);
        await Upload(player, id);
        var original = await (await player.Send(MediaPaths.CharacterPortrait(id))).Content.ReadAsByteArrayAsync();
        if (revoke) host.Access.Cancel(player.Browser);
        var cookies = new Dictionary<string,string>(player.Cookies);
        host.Dispose();
        using var restarted = new Host(directory, clock); using var resumed = restarted.Client();
        foreach (var pair in cookies) resumed.Cookies[pair.Key] = pair.Value;
        var response = await resumed.Send(MediaPaths.CharacterPortrait(id));
        Assert.Equal(revoke ? HttpStatusCode.Forbidden : HttpStatusCode.OK, response.StatusCode);
        if (!revoke) Assert.Equal(original, await response.Content.ReadAsByteArrayAsync());
        Assert.Single(Directory.GetFiles(restarted.App.Services.GetRequiredService<MediaStore>().DirectoryPath));
        restarted.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task CaptureActualIntegratedPagesForResponsiveChecks()
    {
        var directory = Environment.GetEnvironmentVariable("N9_INTEGRATION_HTML_DIRECTORY");
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var owner = AddPlayer(host); var id = Character(host, owner.Profile);
        using var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential); await Enroll(player, dm);
        var routes = new[] { ("editor", player, Editor(id)), ("character", player, $"/player/character/{id}"),
            ("branding", dm, Editor(Guid.Empty, "application")), ("secure-access", player, "/secure-access") };
        foreach (var (name, member, path) in routes)
        {
            var response = await member.Send(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains(name == "secure-access" ? "Authorized as Player" : "Secure authorization", html);
            if (directory is not null) { Directory.CreateDirectory(directory); await File.WriteAllTextAsync(Path.Combine(directory, name + ".html"), html); }
        }
        host.DeleteOnDispose();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task ActualRollbackFencePreservesN9ImagesButRequiresFreshApprovalAfterRestart()
    {
        var host = new Host(); var directory = host.DirectoryPath; var clock = host.Clock;
        using var dm = host.Client(); await dm.Bootstrap();
        var owner = AddPlayer(host); var id = Character(host, owner.Profile);
        using var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential); await Enroll(player, dm);
        await Upload(player, id);
        var image = await (await player.Send(MediaPaths.CharacterPortrait(id))).Content.ReadAsByteArrayAsync();
        var state = Path.Combine(directory, "secure-access", "state.bin");
        var stale = File.ReadAllBytes(state);
        host.Access.Cancel(player.Browser);
        File.WriteAllBytes(state, stale); // Deliberately model whole-data restoration in disposable storage.
        var source = File.ReadAllText(Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..")),
            "hub-updater/runtime/initiativetracker-install-update"));
        var fence = source.Split("# BEGIN SECURE ROLLBACK FENCE\n")[1].Split("# END SECURE ROLLBACK FENCE")[0];
        var info = new System.Diagnostics.ProcessStartInfo("/usr/bin/python3")
            { RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("-I"); info.ArgumentList.Add("-"); info.ArgumentList.Add(directory);
        using (var process = System.Diagnostics.Process.Start(info)!)
        {
            await process.StandardInput.WriteAsync(fence); process.StandardInput.Close();
            await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await player.Send(MediaPaths.CharacterPortrait(id))).StatusCode);
        var cookies = new Dictionary<string,string>(player.Cookies); host.Dispose();
        using var restarted = new Host(directory, clock); using var resumed = restarted.Client();
        foreach (var cookie in cookies) resumed.Cookies[cookie.Key] = cookie.Value;
        Assert.Equal(HttpStatusCode.Forbidden, (await resumed.Send(MediaPaths.CharacterPortrait(id))).StatusCode);
        using var freshDm = restarted.Client(); await freshDm.Bootstrap(); await Enroll(resumed, freshDm);
        Assert.Equal(image, await (await resumed.Send(MediaPaths.CharacterPortrait(id))).Content.ReadAsByteArrayAsync());
        using var check = restarted.Factory.CreateDbContext();
        Assert.Equal(18, check.PlayerCharacters.Single(x => x.Id == id).CurrentHp);
        Assert.Single(check.PresentationAssets);
        restarted.DeleteOnDispose();
    }

    private sealed class GatedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(); await Release.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
