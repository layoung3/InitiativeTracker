using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeTracker.Tests;

public sealed class N9MigrationIntegrationTests
{
    private const string Previous = "20261006230924_AddHubIdentity";
    private const string Presentation = "20261031050702_AddPresentationAssets";
    private static InitiativeTrackerDbContext Database(string directory) => new(
        new DbContextOptionsBuilder<InitiativeTrackerDbContext>().UseSqlite($"Data Source={Path.Combine(directory,"initiative-tracker.db")};Pooling=False").Options);
    private static WebApplicationFactory<EncounterStateService> Application(string directory) =>
        new TestApplicationFactory(directory, builder => builder
            .UseEnvironment("Production").UseSetting("Tracker:DataDirectory",directory));
    private static void CopyStoppedCheckpoint(string source,string destination)
    {
        Directory.CreateDirectory(destination);
        foreach(var path in Directory.GetFiles(source,"*",SearchOption.AllDirectories))
        {
            var target=Path.Combine(destination,Path.GetRelativePath(source,path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(path,target);
        }
    }

    [Fact]
    public async Task ActualFirstStartMigratesStopped015CheckpointAndCoherentRestoreDoesNotRunDown()
    {
        var root=Path.Combine(Path.GetTempPath(),"n9-migration-"+Guid.NewGuid());
        var checkpoint=Path.Combine(root,"checkpoint"); var working=Path.Combine(root,"working"); var restored=Path.Combine(root,"restored");
        Directory.CreateDirectory(checkpoint);
        var hub=Guid.NewGuid(); var session=Guid.NewGuid(); var profile=Guid.NewGuid(); var character=Guid.NewGuid(); var display=Guid.NewGuid();
        try
        {
            using(var db=Database(checkpoint))
            {
                db.GetService<IMigrator>().Migrate(Previous);
                Assert.Equal(Previous,db.Database.GetAppliedMigrations().Last());
                db.AddRange(new ApplicationSettingsRecord{Id=1,HubId=hub,HubHostName="initiative-fixture"},
                    new GameSessionRecord{Id=1,SessionId=session,JoinCode="FIXTURE",DmPin="123456",PlayerJoinToken="fixture-player",DisplayToken="fixture-display",CreatedAtUtc=DateTime.UtcNow},
                    new PlayerProfile{Id=profile,Name="Checkpoint owner"},
                    new PlayerCharacter{Id=character,PlayerProfileId=profile,Name="Checkpoint hero",CurrentHp=13,MaxHp=20},
                    new RegisteredDevice{Id=display,Name="Checkpoint display",IsTrusted=true,PairedAtUtc=DateTime.UtcNow});
                db.SaveChanges();
            }
            var keys=Path.Combine(checkpoint,"DataProtection-Keys"); Directory.CreateDirectory(keys);
            var protectedCheckpoint=DataProtectionProvider.Create(new DirectoryInfo(keys),options=>options.SetApplicationName("InitiativeTracker"))
                .CreateProtector("n9-checkpoint").Protect("same checkpoint keys");
            CopyStoppedCheckpoint(checkpoint,working);
            var image=N9MediaTests.Raster(); var blob=Guid.NewGuid();
            using(var app=Application(working))
            {
                using var client=app.CreateClient(); Assert.True((await client.GetAsync("/system/health")).IsSuccessStatusCode);
                using var db=app.Services.GetRequiredService<IDbContextFactory<InitiativeTrackerDbContext>>().CreateDbContext();
                Assert.Equal(Presentation,db.Database.GetAppliedMigrations().Last()); Assert.Empty(db.Database.GetPendingMigrations());
                Assert.Equal(hub,db.ApplicationSettings.Single().HubId); Assert.Equal(session,db.GameSessions.Single().SessionId);
                Assert.Equal(13,db.PlayerCharacters.Single(x=>x.Id==character).CurrentHp); Assert.True(db.RegisteredDevices.Single(x=>x.Id==display).IsTrusted);
                db.Add(new PresentationSettings{Id=1,ApplicationName="Migrated campaign",ShowApplicationLogo=true});
                db.Add(new PresentationAsset{Scope="character",SubjectId=character,Kind="portrait",OwnerProfileId=profile,BlobId=blob,Length=image.Length,Width=80,Height=40}); db.SaveChanges();
                File.WriteAllBytes(Path.Combine(working,"Media",blob.ToString("N")+".png"),image);
            }
            using(var app=Application(working))
            {
                using var client=app.CreateClient(); Assert.True((await client.GetAsync("/system/health")).IsSuccessStatusCode);
                using var db=app.Services.GetRequiredService<IDbContextFactory<InitiativeTrackerDbContext>>().CreateDbContext();
                Assert.Empty(db.Database.GetPendingMigrations()); Assert.Equal("Migrated campaign",db.PresentationSettings.Single().ApplicationName);
                Assert.Equal(image,app.Services.GetRequiredService<MediaStore>().Read("character",character,"portrait"));
                Assert.Equal("same checkpoint keys",DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(working,"DataProtection-Keys")),options=>options.SetApplicationName("InitiativeTracker"))
                    .CreateProtector("n9-checkpoint").Unprotect(protectedCheckpoint));
            }
            // Stopped full-checkpoint restoration to empty disposable storage, never Down
            // and never an old binary against the newer schema. Secure-state fencing
            // is independently exercised through the actual reviewed updater helper.
            CopyStoppedCheckpoint(checkpoint,restored);
            using(var db=Database(restored))
            {
                Assert.Equal(Previous,db.Database.GetAppliedMigrations().Last());
                Assert.Equal(hub,db.ApplicationSettings.Single().HubId); Assert.Equal(session,db.GameSessions.Single().SessionId);
                Assert.Equal(profile,db.PlayerCharacters.Single(x=>x.Id==character).PlayerProfileId);
                Assert.True(db.RegisteredDevices.Single(x=>x.Id==display).IsTrusted);
                using var command=db.Database.GetDbConnection().CreateCommand(); db.Database.OpenConnection();
                command.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('PresentationAssets','PresentationSettings')";
                Assert.Equal(0L,command.ExecuteScalar());
            }
            Assert.Equal("same checkpoint keys",DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(restored,"DataProtection-Keys")),options=>options.SetApplicationName("InitiativeTracker"))
                .CreateProtector("n9-checkpoint").Unprotect(protectedCheckpoint));
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
