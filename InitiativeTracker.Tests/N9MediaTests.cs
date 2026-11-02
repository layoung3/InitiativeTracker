using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace InitiativeTracker.Tests;

public sealed class N9MediaTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "n9-media-" + Guid.NewGuid());
    private readonly WebApplicationFactory<EncounterStateService> app;
    private readonly HttpClient client;
    private IDbContextFactory<InitiativeTrackerDbContext> Factory => app.Services.GetRequiredService<IDbContextFactory<InitiativeTrackerDbContext>>();
    private MediaStore Store => app.Services.GetRequiredService<MediaStore>();
    private EncounterStateService State => app.Services.GetRequiredService<EncounterStateService>();
    public N9MediaTests()
    {
        app = new TestApplicationFactory(directory, builder => builder
            .UseEnvironment("Development").UseSetting("Tracker:DataDirectory", directory)
            .ConfigureServices(services =>
            {
                // Pruning is invoked explicitly in these storage tests; do not race a timer.
                foreach(var hosted in services.Where(item=>item.ServiceType==typeof(Microsoft.Extensions.Hosting.IHostedService) && item.ImplementationType==typeof(MediaMaintenanceService)).ToArray())
                    services.Remove(hosted);
            }));
        client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private Guid Character(Guid owner)
    {
        using var db = Factory.CreateDbContext();
        if (!db.PlayerProfiles.Any(x => x.Id == owner)) db.PlayerProfiles.Add(new PlayerProfile {Id=owner,Name="Fixture player"});
        var character = new PlayerCharacter {PlayerProfileId=owner,Name="Fixture hero",MaxHp=20,CurrentHp=13};
        db.PlayerCharacters.Add(character); db.SaveChanges(); return character.Id;
    }

    private Guid SignIn(string role, Guid? owner = null, bool stale = false, bool revoked = false)
    {
        var session = app.Services.GetRequiredService<GameSessionService>().CurrentSession.SessionId;
        var claims = new List<Claim> {new(ClaimTypes.Role,role),new("session_id",(stale?Guid.NewGuid():session).ToString())};
        Guid deviceId = Guid.NewGuid();
        using (var db = Factory.CreateDbContext())
        {
            if (owner.HasValue)
            {
                var credential = new PlayerDeviceCredential {PlayerProfileId=owner.Value,TokenHash=Guid.NewGuid().ToString(),RevokedAtUtc=revoked?DateTime.UtcNow:null};
                var player = new PlayerConnection {PlayerProfileId=owner.Value,SessionId=session,PlayerDeviceCredentialId=credential.Id,Name="Fixture player"};
                db.Add(credential); db.Add(player); db.SaveChanges();
                claims.AddRange([new("player_id",player.Id.ToString()),new("player_profile_id",owner.Value.ToString()),new("player_device_id",credential.Id.ToString())]);
            }
            if (role=="Display")
            {
                db.RegisteredDevices.Add(new RegisteredDevice {Id=deviceId,Name="Fixture display",IsTrusted=!revoked}); db.SaveChanges();
                claims.Add(new("device_id",deviceId.ToString()));
            }
        }
        var scheme=CookieAuthenticationDefaults.AuthenticationScheme;
        var options=app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
        var ticket=new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims,scheme)),new AuthenticationProperties(),scheme);
        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add("Cookie",options.Cookie.Name+"="+options.TicketDataFormat.Protect(ticket));
        return deviceId;
    }

    private static string Editor(Guid id, string scope="character") => $"/appearance/{scope}/{id:D}";
    private async Task<string> Token(Guid id,string scope="character")
    {
        var response=await client.GetAsync(Editor(id,scope)); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        return WebUtility.HtmlDecode(Regex.Match(await response.Content.ReadAsStringAsync(),"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    }
    private async Task<HttpResponseMessage> Upload(Guid id,byte[] bytes,string token,string kind="portrait",string scope="character",string filename="image.png")
    {
        using var form=new MultipartFormDataContent();
        form.Add(new StringContent(token),"__RequestVerificationToken"); form.Add(new StringContent(kind),"kind");
        var file=new ByteArrayContent(bytes); file.Headers.ContentType=new MediaTypeHeaderValue("image/png"); form.Add(file,"Upload",filename);
        return await client.PostAsync(Editor(id,scope)+"?handler=Upload",form);
    }
    private async Task<HttpResponseMessage> Remove(Guid id,string token,string kind="portrait",string scope="character") => await client.PostAsync(Editor(id,scope)+"?handler=Remove",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token},{"kind",kind}}));

    public static byte[] Raster(int width=80,int height=40,SKEncodedImageFormat format=SKEncodedImageFormat.Png)
    {
        using var bitmap=new SKBitmap(width,height); bitmap.Erase(SKColors.CornflowerBlue);
        using var bytes=bitmap.Encode(format,95); return bytes.ToArray();
    }
    public static byte[] Chunk(string type,byte[] content)
    {
        using var memory=new MemoryStream(); var length=new byte[4]; BinaryPrimitives.WriteInt32BigEndian(length,content.Length); memory.Write(length);
        var body=Encoding.ASCII.GetBytes(type).Concat(content).ToArray(); memory.Write(body);
        uint crc=uint.MaxValue;
        foreach(var b in body) {crc^=b; for(int i=0;i<8;i++) crc=(crc&1)!=0 ? (crc>>1)^0xedb88320 : crc>>1;}
        BinaryPrimitives.WriteUInt32BigEndian(length,~crc); memory.Write(length); return memory.ToArray();
    }
    private static byte[] AddChunk(byte[] png,string type,byte[] content) => png[..^12].Concat(Chunk(type,content)).Concat(png[^12..]).ToArray();

    [Fact]
    public async Task PngIsResizedMetadataStrippedAndStoredWithGeneratedNameOutsideRelease()
    {
        var owner=Guid.NewGuid(); var id=Character(owner); SignIn("Player",owner);
        var input=AddChunk(Raster(1700,900),"tEXt",Encoding.ASCII.GetBytes("GPS\0private-location-fixture"));
        var response=await Upload(id,input,await Token(id),filename:"../../shell.cshtml");
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        var media=await client.GetAsync(MediaPaths.CharacterPortrait(id));
        Assert.Equal("image/png",media.Content.Headers.ContentType!.MediaType);
        Assert.Contains("no-store",media.Headers.CacheControl!.ToString()); Assert.Contains("private",media.Headers.CacheControl.ToString());
        Assert.Equal("nosniff",media.Headers.GetValues("X-Content-Type-Options").Single());
        var bytes=await media.Content.ReadAsByteArrayAsync(); Assert.DoesNotContain("private-location-fixture",Encoding.Latin1.GetString(bytes));
        using var image=SKBitmap.Decode(bytes); Assert.Equal(512,image.Width); Assert.Equal(271,image.Height);
        var file=Assert.Single(Directory.GetFiles(Store.DirectoryPath)); Assert.Matches(@"^[a-f0-9]{32}\.png$",Path.GetFileName(file));
        Assert.StartsWith(directory,file); Assert.DoesNotContain("wwwroot",file);
        if(!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead|UnixFileMode.UserWrite,File.GetUnixFileMode(file));
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("GIF89a")]
    [InlineData("RIFF1234WEBP")]
    [InlineData("not an image")]
    public async Task UnsupportedAndMalformedImagesNeverPersist(string input)
    {
        var owner=Guid.NewGuid(); var id=Character(owner); SignIn("Player",owner);
        Assert.Equal(HttpStatusCode.BadRequest,(await Upload(id,Encoding.UTF8.GetBytes(input),await Token(id))).StatusCode);
        Assert.Empty(Directory.GetFiles(Store.DirectoryPath)); using var db=Factory.CreateDbContext(); Assert.Empty(db.PresentationAssets);
    }

    [Fact]
    public async Task TruncatedAnimatedAndOversizedImagesPreservePreviousAsset()
    {
        var owner=Guid.NewGuid(); var id=Character(owner); SignIn("Player",owner); var token=await Token(id);
        Assert.Equal(HttpStatusCode.Redirect,(await Upload(id,Raster(),token)).StatusCode);
        var previous=Store.Read("character",id,"portrait");
        var animation=AddChunk(Raster(),"acTL",new byte[]{0,0,0,2,0,0,0,0});
        foreach(var bad in new[]{Raster()[..^5],Raster(format:SKEncodedImageFormat.Jpeg)[..^2],animation,new byte[ImageNormalizer.MaxUploadBytes+1]})
        {
            Assert.Equal(HttpStatusCode.BadRequest,(await Upload(id,bad,token)).StatusCode);
            Assert.Equal(previous,Store.Read("character",id,"portrait")); Assert.Single(Directory.GetFiles(Store.DirectoryPath));
        }
    }

    [Theory]
    [InlineData(5000,1)]
    [InlineData(4096,4096)]
    public async Task DecompressionRiskDimensionsAreRejectedBeforeFullDecode(int width,int height)
    {
        var owner=Guid.NewGuid(); var id=Character(owner); SignIn("Player",owner);
        var png=Raster(); var ihdr=png[16..29]; BinaryPrimitives.WriteInt32BigEndian(ihdr,width); BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4),height);
        var bomb=png[..8].Concat(Chunk("IHDR",ihdr)).Concat(png[33..]).ToArray();
        var response=await Upload(id,bomb,await Token(id)); Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        Assert.Contains("12 million",await response.Content.ReadAsStringAsync()); Assert.Empty(Directory.GetFiles(Store.DirectoryPath));
    }

    [Fact]
    public async Task CsrfCrossPlayerAndProfilePrivacyAreEnforced()
    {
        var owner=Guid.NewGuid(); var other=Guid.NewGuid(); var id=Character(owner); var otherId=Character(other);
        SignIn("Player",owner); var token=await Token(id);
        Assert.Equal(HttpStatusCode.BadRequest,(await Upload(id,Raster(),"")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await Upload(id,Raster(),token)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await Upload(owner,Raster(),await Token(owner,"profile"),scope:"profile")).StatusCode);
        SignIn("Player",other); token=await Token(otherId);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(MediaPaths.CharacterPortrait(id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(MediaPaths.ProfilePortrait(owner))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Upload(id,Raster(),token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await Remove(id,token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(Editor(id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(Editor(Guid.Empty,"application"))).StatusCode);
        SignIn("DM"); Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(MediaPaths.ProfilePortrait(owner))).StatusCode);
    }

    [Fact]
    public async Task PublicProjectionControlsOldUrlsAfterHideRemoveAndDisplayRevocation()
    {
        var owner=Guid.NewGuid(); var id=Character(owner); SignIn("Player",owner);
        await Upload(id,Raster(),await Token(id)); State.AddPlayerCharacter(id);
        var device=SignIn("Display"); var url=MediaPaths.CharacterPortrait(id)+"?v=old";
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(url)).StatusCode);
        var combatant=State.CurrentEncounter.Combatants.Single();
        // Existing assigned-player exception stays exactly as before, including HP visibility.
        State.SetCombatantVisibility(combatant.Id,true,true);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(url)).StatusCode);
        Assert.Equal(13,State.GetDisplayState().Combatants.Single().CurrentHp);
        combatant=State.CurrentEncounter.Combatants.Single(); combatant.AssignedPlayerProfileId=null;
        Assert.Empty(State.GetDisplayState().Combatants);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(url)).StatusCode);
        State.SetCombatantVisibility(combatant.Id,false,true);
        Assert.Null(State.GetDisplayState().Combatants.Single().CurrentHp);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(url)).StatusCode);
        State.CurrentEncounter.Combatants.Clear();
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(url)).StatusCode);
        using(var db=Factory.CreateDbContext()){db.RegisteredDevices.Single(x=>x.Id==device).IsTrusted=false;db.SaveChanges();}
        Assert.NotEqual(HttpStatusCode.OK,(await client.GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData(true,false)]
    [InlineData(false,true)]
    public async Task RevokedAndStalePlayersCannotReadOrChangeMedia(bool revoked,bool stale)
    {
        var owner=Guid.NewGuid(); var id=Character(owner); SignIn("Player",owner); var token=await Token(id);
        await Upload(id,Raster(),token);
        SignIn("Player",owner,stale,revoked);
        Assert.NotEqual(HttpStatusCode.OK,(await client.GetAsync(MediaPaths.CharacterPortrait(id))).StatusCode);
        var denied = await Upload(id,Raster(),token);
        Assert.Equal(HttpStatusCode.Redirect,denied.StatusCode);
        Assert.StartsWith("/join",denied.Headers.Location!.OriginalString);
        using var db=Factory.CreateDbContext(); Assert.Single(db.PresentationAssets);
    }

    [Fact]
    public async Task ReplacementRemovalMissingFilesAndDeletionCleanUp()
    {
        var owner=Guid.NewGuid(); var id=Character(owner); SignIn("Player",owner); var token=await Token(id);
        await Upload(id,Raster(),token); var old=Assert.Single(Directory.GetFiles(Store.DirectoryPath));
        await Upload(id,Raster(120,90),token); Assert.False(File.Exists(old)); Assert.Single(Directory.GetFiles(Store.DirectoryPath));
        File.Delete(Directory.GetFiles(Store.DirectoryPath).Single());
        Assert.Equal(MediaPaths.DefaultPortrait,(await client.GetAsync(MediaPaths.CharacterPortrait(id))).Headers.Location!.OriginalString);
        await Upload(id,Raster(),token); await Remove(id,token);
        Assert.Empty(Directory.GetFiles(Store.DirectoryPath));
        await Upload(id,Raster(),token);
        using(var db=Factory.CreateDbContext()){db.PlayerCharacters.Remove(db.PlayerCharacters.Single(x=>x.Id==id));db.SaveChanges();}
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(MediaPaths.CharacterPortrait(id))).StatusCode);
        await Store.PruneAsync(); Assert.Empty(Directory.GetFiles(Store.DirectoryPath));
        using(var db=Factory.CreateDbContext()) Assert.Empty(db.PresentationAssets);
    }

    [Fact]
    public async Task CommitFailureAndStorageQuotaLeaveOldImageIntact()
    {
        var owner=Guid.NewGuid(); var id=Character(owner); SignIn("Player",owner); var token=await Token(id);
        await Upload(id,Raster(),token); var previous=Store.Read("character",id,"portrait");
        using(var db=Factory.CreateDbContext()) db.Database.ExecuteSqlRaw("CREATE TRIGGER N9_Fail BEFORE UPDATE ON PresentationAssets BEGIN SELECT RAISE(ABORT, 'disposable fault'); END;");
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await Upload(id,Raster(200,100),token)).StatusCode);
        Assert.Equal(previous,Store.Read("character",id,"portrait")); Assert.Single(Directory.GetFiles(Store.DirectoryPath));
        using(var db=Factory.CreateDbContext()) db.Database.ExecuteSqlRaw("DROP TRIGGER N9_Fail;");
        using(var file=File.Create(Path.Combine(Store.DirectoryPath,"reserved-fixture.bin"))) file.SetLength(MediaStore.MaxStorageBytes);
        Assert.Equal(HttpStatusCode.BadRequest,(await Upload(id,Raster(200,100),token)).StatusCode);
        Assert.Equal(previous,Store.Read("character",id,"portrait"));
    }

    [Fact]
    public async Task CampaignBannerAndReplaceablePublicLogoRequireDmChanges()
    {
        SignIn("DM"); var token=await Token(Guid.Empty,"application");
        Assert.Equal(HttpStatusCode.Redirect,(await Upload(Guid.Empty,Raster(2400,1200),token,"banner","application")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await Upload(Guid.Empty,Raster(90,70),token,"logo","application")).StatusCode);
        using(var image=SKBitmap.Decode(Store.Read("application",Guid.Empty,"banner"))) {Assert.Equal(800,image.Width);Assert.Equal(400,image.Height);}
        var settings=new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token},{"ApplicationName","<img src=x onerror=alert(1)>"},{"ShowApplicationLogo","true"}});
        Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync(Editor(Guid.Empty,"application")+"?handler=Settings",settings)).StatusCode);
        client.DefaultRequestHeaders.Remove("Cookie");
        var logo=await client.GetAsync("/branding/logo"); Assert.Equal(HttpStatusCode.OK,logo.StatusCode);
        using(var image=SKBitmap.Decode(await logo.Content.ReadAsByteArrayAsync())) Assert.Equal(90,image.Width);
        Assert.NotEqual(HttpStatusCode.OK,(await client.GetAsync(MediaPaths.CampaignBanner)).StatusCode);
        var join=await client.GetStringAsync("/join"); Assert.Contains("&lt;img",join); Assert.DoesNotContain("<img src=x",join);
    }

    [Fact]
    public async Task DecodingConcurrencyIsBoundedAndRevocationDuringReadPreventsCommit()
    {
        var owner=Guid.NewGuid(); var id=Character(owner);
        var input=Raster();
        using var source=new GatedStream(input);
        var file=new FormFile(source,0,input.Length,"Upload","photo.png");
        bool authorized=true;
        MediaSubject? Authorize() => authorized ? new("Fixture",owner) : null;
        var first=Store.ReplaceAsync("character",id,"portrait",file,Authorize, action => action());
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<MediaBusyException>(() => Store.ReplaceAsync("character",id,"banner",file,Authorize, action => action()));
        authorized=false;
        source.Release.TrySetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => first);
        Assert.Empty(Directory.GetFiles(Store.DirectoryPath));
        authorized=true;
        using var valid=new MemoryStream(input);
        await Store.ReplaceAsync("character",id,"portrait",new FormFile(valid,0,input.Length,"Upload","photo.png"),Authorize, action => action());
        Assert.NotNull(Store.Read("character",id,"portrait"));
    }

    [Fact]
    public async Task PerProfileQuotaAndOrphanRecoverySurviveServiceRecreation()
    {
        var owner=Guid.NewGuid(); var id=Character(owner); var reserved=Character(owner); SignIn("Player",owner);
        using(var db=Factory.CreateDbContext())
        {
            db.PresentationAssets.Add(new PresentationAsset{Scope="character",SubjectId=reserved,Kind="banner",BlobId=Guid.NewGuid(),OwnerProfileId=owner,Length=MediaStore.MaxProfileBytes});
            db.SaveChanges();
        }
        Assert.Equal(HttpStatusCode.BadRequest,(await Upload(id,Raster(),await Token(id))).StatusCode);
        using(var db=Factory.CreateDbContext()){db.PresentationAssets.RemoveRange(db.PresentationAssets);db.SaveChanges();}
        await Upload(id,Raster(),await Token(id));
        var orphan=Path.Combine(Store.DirectoryPath,Guid.NewGuid().ToString("N")+".png");
        var interrupted=Path.Combine(Store.DirectoryPath,Guid.NewGuid().ToString("N")+".tmp");
        File.WriteAllBytes(orphan,Raster()); File.WriteAllBytes(interrupted,new byte[]{1,2});
        var recreated=new MediaStore(Factory,new ImageNormalizer(),Microsoft.Extensions.Logging.Abstractions.NullLogger<MediaStore>.Instance,directory);
        Assert.NotNull(recreated.Read("character",id,"portrait"));
        await recreated.PruneAsync(); Assert.False(File.Exists(orphan));Assert.False(File.Exists(interrupted));
        Assert.Single(Directory.GetFiles(Store.DirectoryPath));
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("remove")]
    [InlineData("settings")]
    public async Task RejectedCommitRollsBackAndNeverPrunesReferencedImages(string operation)
    {
        var owner=Guid.NewGuid(); var id=Character(owner); SignIn("Player",owner);
        await Upload(id,Raster(),await Token(id));
        var settings=app.Services.GetRequiredService<BrandingService>();
        settings.Save("Original",true, action=>action());
        Guid previous;
        Guid inactiveId;
        using(var db=Factory.CreateDbContext())
        {
            previous=db.PresentationAssets.Single().BlobId;
            var inactive=new PlayerCharacter{PlayerProfileId=owner,Name="Inactive art",IsActive=false};
            var old=new PresentationAsset{Scope="character",SubjectId=inactive.Id,Kind="portrait",BlobId=Guid.NewGuid(),OwnerProfileId=owner,Length=3};
            db.AddRange(inactive,old); db.SaveChanges(); inactiveId=inactive.Id;
            File.WriteAllBytes(Path.Combine(Store.DirectoryPath,old.BlobId.ToString("N")+".png"),new byte[]{1,2,3});
        }
        var files=Directory.GetFiles(Store.DirectoryPath).ToDictionary(path=>Path.GetFileName(path),File.ReadAllBytes);
        int Deny(Func<int> commit)
        {
            using var check=Factory.CreateDbContext();
            Assert.Equal(previous,check.PresentationAssets.Single(x=>x.SubjectId==id).BlobId);
            Assert.Equal("Original",settings.ApplicationName);
            throw new UnauthorizedAccessException();
        }
        if(operation=="replace")
        {
            var input=Raster(200,100); using var stream=new MemoryStream(input);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Store.ReplaceAsync("character",id,"portrait",new FormFile(stream,0,input.Length,"Upload","fixture.png"),()=>new("Unit storage fixture",owner),Deny));
        }
        if(operation=="remove") await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Store.RemoveAsync("character",id,"portrait",()=>new("Unit storage fixture",owner),Deny));
        if(operation=="settings") Assert.Throws<UnauthorizedAccessException>(()=>settings.Save("Rejected",false,Deny));
        using(var check=Factory.CreateDbContext())
        {
            Assert.Equal(previous,check.PresentationAssets.Single(x=>x.SubjectId==id).BlobId);
            Assert.Single(check.PresentationAssets.Where(x=>x.SubjectId==inactiveId));
            Assert.Equal("Original",check.PresentationSettings.Single().ApplicationName);
        }
        Assert.Equal(files.Keys.Order(),Directory.GetFiles(Store.DirectoryPath).Select(Path.GetFileName).Order());
        foreach(var file in files) Assert.Equal(file.Value,File.ReadAllBytes(Path.Combine(Store.DirectoryPath,file.Key)));
    }

    [Fact]
    public async Task ProductionPrivateMediaRequiresHttps()
    {
        var owner=Guid.NewGuid();var id=Character(owner);SignIn("Player",owner);
        await Upload(id,Raster(),await Token(id));
        using var production=new TestApplicationFactory(directory, builder=>builder
            .UseEnvironment("Production").UseSetting("Tracker:DataDirectory",directory));
        using var http=production.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        http.DefaultRequestHeaders.Add("Cookie",client.DefaultRequestHeaders.GetValues("Cookie"));
        Assert.Equal((HttpStatusCode)426,(await http.GetAsync(MediaPaths.CharacterPortrait(id))).StatusCode);
        using var https=production.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false,BaseAddress=new Uri("https://localhost")});
        https.DefaultRequestHeaders.Add("Cookie",client.DefaultRequestHeaders.GetValues("Cookie"));
        Assert.Equal(HttpStatusCode.NotFound,(await https.GetAsync(MediaPaths.CharacterPortrait(id))).StatusCode); // HTTPS without the configured foundation grants nothing.
        // TestServer exercises transport policy; this is not actual certificate/TLS validation.
    }

    [SymbolicLinkFact]
    public async Task SymbolicLinkNeverServesAnArbitraryServerFile()
    {
        var owner=Guid.NewGuid();var id=Character(owner);SignIn("Player",owner);
        await Upload(id,Raster(),await Token(id));
        var file=Directory.GetFiles(Store.DirectoryPath).Single();File.Delete(file);
        var secret=Path.Combine(directory,"not-an-image-secret"); File.WriteAllText(secret,"server-file-must-not-leak");
        File.CreateSymbolicLink(file,secret);
        var response=await client.GetAsync(MediaPaths.CharacterPortrait(id));
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);Assert.Equal(MediaPaths.DefaultPortrait,response.Headers.Location!.OriginalString);
        Assert.DoesNotContain("server-file-must-not-leak",await response.Content.ReadAsStringAsync());
    }

    private sealed class GatedStream(byte[] bytes):MemoryStream(bytes)
    {
        public TaskCompletionSource Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default)
        {
            Started.TrySetResult(); await Release.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer,cancellationToken);
        }
    }

    public void Dispose(){client.Dispose();app.Dispose();if(Directory.Exists(directory))Directory.Delete(directory,true);}
}
