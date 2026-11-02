using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Infrastructure;
using InitiativeTracker.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Http;
using static InitiativeTracker.Tests.SecureAccessWebTests;

namespace InitiativeTracker.Tests;

public sealed class N9CommitBoundaryTests
{
    private sealed class Barrier : SaveChangesInterceptor
    {
        public bool Armed;
        public bool PauseAtCommit;
        public bool FailCommit;
        public bool FailAfterCommit;
        public int Sequence;
        public int CommittedSequence;
        public readonly TaskCompletionSource Arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Resume = new(false);
        private readonly HashSet<Guid> contexts = [];
        public readonly TransactionBarrier Transactions;
        public Barrier() { Transactions = new(this); }
        public override InterceptionResult<int> SavingChanges(DbContextEventData data, InterceptionResult<int> result)
        {
            if (Armed && data.Context is { } context && context.ChangeTracker.Entries().Any(entry =>
                entry.Entity is PresentationAsset or PresentationSettings && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
                contexts.Add(context.ContextId.InstanceId);
            return result;
        }
        private bool Matches(DbContext? context) => Armed && context is not null && contexts.Contains(context.ContextId.InstanceId);
        public void Pause()
        {
            Arrived.TrySetResult();
            if (!Resume.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Disposable precommit barrier timed out.");
        }
        public sealed class TransactionBarrier(Barrier owner) : DbTransactionInterceptor
        {
            public override InterceptionResult TransactionCommitting(DbTransaction transaction, TransactionEventData data, InterceptionResult result)
            {
                if (owner.Matches(data.Context))
                {
                    if (owner.FailCommit) throw new IOException("Disposable failure before SQLite commit.");
                    if (owner.PauseAtCommit) owner.Pause();
                }
                return result;
            }
            public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData data)
            {
                if (!owner.Matches(data.Context)) return;
                owner.CommittedSequence = Interlocked.Increment(ref owner.Sequence);
                if (owner.FailAfterCommit) throw new IOException("Disposable ambiguous reply after SQLite commit.");
            }
        }
    }

    private sealed class PausingCommitter(IHttpContextAccessor contexts, Barrier barrier) : N9PresentationCommitter(contexts)
    {
        public override int Commit(Func<int> commit)
        {
            if (barrier.Armed && !barrier.PauseAtCommit) barrier.Pause();
            return base.Commit(commit); // Real shared service; the test decorator grants no authority.
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly Barrier Barrier;
        public readonly Host Host;
        public readonly Session Dm;
        public readonly Session Player;
        public readonly Guid CharacterId;
        public Guid InactiveId;
        public Guid PreviousBlob;
        public string PreviousName = "";
        private string DmToken = "", PlayerToken = "";
        private Guid DmGrant, PlayerGrant;
        public Dictionary<string,byte[]> PreviousFiles = [];
        private Fixture(Host host, Session dm, Session player, Guid character, Barrier barrier)
        { Host = host; Dm = dm; Player = player; CharacterId = character; Barrier = barrier; }
        public static async Task<Fixture> Create()
        {
            var barrier = new Barrier();
            var host = new Host(configure: services =>
            {
                services.AddDbContextFactory<InitiativeTrackerDbContext>(options => options.AddInterceptors(barrier, barrier.Transactions));
                services.RemoveAll<N9PresentationCommitter>();
                services.AddSingleton<N9PresentationCommitter>(provider => new PausingCommitter(provider.GetRequiredService<IHttpContextAccessor>(), barrier));
                // Isolate user-mutation pruning from independent scheduled maintenance.
                foreach (var hosted in services.Where(item => item.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) && item.ImplementationType == typeof(MediaMaintenanceService)).ToArray())
                    services.Remove(hosted);
            });
            var dm = host.Client(); await dm.Bootstrap();
            var owner = AddPlayer(host);
            Guid id;
            using (var db = host.Factory.CreateDbContext())
            {
                var character = new PlayerCharacter { PlayerProfileId = owner.Profile, Name = "Commit boundary hero" };
                db.Add(character); db.SaveChanges(); id = character.Id;
            }
            var player = host.Client(); player.Legacy("Player", owner.Profile, owner.Player, owner.Credential); await Enroll(player, dm);
            var fixture = new Fixture(host, dm, player, id, barrier);
            Assert.Equal(HttpStatusCode.Redirect, (await fixture.Mutate("replace")).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await fixture.Mutate("branding", "Original brand")).StatusCode);
            // A referenced inactive asset must not be pruned by a rejected user mutation.
            using (var db = host.Factory.CreateDbContext())
            {
                var inactive = new PlayerCharacter { PlayerProfileId = owner.Profile, Name = "Inactive private art", IsActive = false };
                var asset = new PresentationAsset { Scope = "character", SubjectId = inactive.Id, Kind = "portrait", BlobId = Guid.NewGuid(), OwnerProfileId = owner.Profile, Length = 3 };
                db.AddRange(inactive, asset); db.SaveChanges(); fixture.InactiveId = inactive.Id;
                File.WriteAllBytes(Path.Combine(fixture.Store.DirectoryPath, asset.BlobId.ToString("N") + ".png"), [4,5,6]);
            }
            using (var db = host.Factory.CreateDbContext())
            {
                fixture.PreviousBlob = db.PresentationAssets.Single(x => x.SubjectId == id).BlobId;
                fixture.PreviousName = db.PresentationSettings.Single().ApplicationName;
            }
            fixture.PreviousFiles = Directory.GetFiles(fixture.Store.DirectoryPath).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
            fixture.DmToken = await dm.Token("/secure-access");
            fixture.PlayerToken = await player.Token("/secure-access");
            fixture.DmGrant = host.Access.Grants().Single(grant => grant.Browser == dm.Browser).Id;
            fixture.PlayerGrant = host.Access.Grants().Single(grant => grant.Browser == player.Browser).Id;
            return fixture;
        }
        public MediaStore Store => Host.App.Services.GetRequiredService<MediaStore>();
        private string Editor(string operation) => operation == "branding" ? "/appearance/application/00000000-0000-0000-0000-000000000000" : $"/appearance/character/{CharacterId}";
        private Session Actor(string operation) => operation == "branding" ? Dm : Player;
        public async Task<HttpResponseMessage> Mutate(string operation, string name = "Changed brand")
        {
            var member = Actor(operation); var path = Editor(operation);
            var token = await member.Token(path);
            if (operation == "branding") return await member.Send(path + "?handler=Settings", new FormUrlEncodedContent(new Dictionary<string,string>
                { ["__RequestVerificationToken"] = token, ["ApplicationName"] = name, ["ShowApplicationLogo"] = "true" }));
            if (operation == "remove") return await member.Send(path + "?handler=Remove", new FormUrlEncodedContent(new Dictionary<string,string>
                { ["__RequestVerificationToken"] = token, ["kind"] = "portrait" }));
            using var body = new MultipartFormDataContent();
            body.Add(new StringContent(token), "__RequestVerificationToken"); body.Add(new StringContent("portrait"), "kind");
            var file = new ByteArrayContent(N9MediaTests.Raster(200,100)); file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            body.Add(file, "Upload", "fixture.png"); return await member.Send(path + "?handler=Upload", body);
        }
        public async Task Revoke(string operation, bool cancel)
        {
            // Actual HTTPS Razor handler including origin/CSRF/current-grant checks.
            // Tokens and target IDs are obtained before the transaction barrier.
            var target = Actor(operation);
            using var response = cancel
                ? await target.PostToken("Cancel", operation == "branding" ? DmToken : PlayerToken)
                : await Dm.PostToken("Revoke", DmToken, ("id", (operation == "branding" ? DmGrant : PlayerGrant).ToString()));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }
        public void AssertPreserved()
        {
            using var db = Host.Factory.CreateDbContext();
            Assert.Equal(PreviousBlob, db.PresentationAssets.Single(x => x.SubjectId == CharacterId).BlobId);
            Assert.Equal(PreviousName, db.PresentationSettings.Single().ApplicationName);
            Assert.Single(db.PresentationAssets.Where(x => x.SubjectId == InactiveId));
            var files = Directory.GetFiles(Store.DirectoryPath).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
            Assert.Equal(PreviousFiles.Keys.Order(), files.Keys.Order());
            foreach (var file in PreviousFiles) Assert.Equal(file.Value, files[file.Key]);
        }
        public void Dispose()
        {
            Barrier.Resume.Set(); Player.Dispose(); Dm.Dispose(); Host.Dispose(); Host.DeleteOnDispose(); Barrier.Resume.Dispose();
        }
    }

    [LinuxTheory("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    [InlineData("replace", "revoke")]
    [InlineData("replace", "cancel")]
    [InlineData("replace", "expire")]
    [InlineData("remove", "revoke")]
    [InlineData("remove", "cancel")]
    [InlineData("remove", "expire")]
    [InlineData("branding", "revoke")]
    [InlineData("branding", "cancel")]
    [InlineData("branding", "expire")]
    public async Task CompletedRevocationOrExpiryBeforeCommitPreservesPriorState(string operation, string interference)
    {
        using var fixture = await Fixture.Create(); fixture.Barrier.Armed = true;
        var mutation = Task.Run(() => fixture.Mutate(operation));
        try
        {
            await fixture.Barrier.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (interference == "expire") fixture.Host.Clock.Advance(TimeSpan.FromHours(12));
            else await Task.Run(() => fixture.Revoke(operation, interference == "cancel")).WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Barrier.Resume.Set();
            Assert.Equal(HttpStatusCode.Forbidden, (await mutation).StatusCode);
            fixture.AssertPreserved();
        }
        finally { fixture.Barrier.Resume.Set(); await mutation; }
    }

    [LinuxTheory("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    [InlineData("replace", false)]
    [InlineData("replace", true)]
    [InlineData("remove", false)]
    [InlineData("remove", true)]
    [InlineData("branding", false)]
    [InlineData("branding", true)]
    public async Task CommitAlreadyInProgressCompletesBeforeRevocationCanFinish(string operation, bool cancel)
    {
        using var fixture = await Fixture.Create(); fixture.Barrier.PauseAtCommit = true; fixture.Barrier.Armed = true;
        var mutation = Task.Run(() => fixture.Mutate(operation));
        Task? revocation = null;
        var revokeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int revokedSequence = 0;
        try
        {
            await fixture.Barrier.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            revocation = Task.Run(async () => { revokeStarted.SetResult(); await fixture.Revoke(operation, cancel); revokedSequence = Interlocked.Increment(ref fixture.Barrier.Sequence); });
            await revokeStarted.Task;
            Assert.NotSame(revocation, await Task.WhenAny(revocation, Task.Delay(150)));
            fixture.Barrier.Resume.Set();
            Assert.Equal(HttpStatusCode.Redirect, (await mutation).StatusCode);
            await revocation;
            Assert.True(fixture.Barrier.CommittedSequence > 0 && fixture.Barrier.CommittedSequence < revokedSequence);
            using var db = fixture.Host.Factory.CreateDbContext();
            if (operation == "remove") Assert.Empty(db.PresentationAssets.Where(x => x.SubjectId == fixture.CharacterId));
            if (operation == "replace") Assert.NotEqual(fixture.PreviousBlob, db.PresentationAssets.Single(x => x.SubjectId == fixture.CharacterId).BlobId);
            if (operation == "branding") Assert.Equal("Changed brand", db.PresentationSettings.Single().ApplicationName);
            Assert.Single(db.PresentationAssets.Where(x => x.SubjectId == fixture.InactiveId));
        }
        finally { fixture.Barrier.Resume.Set(); await mutation; if (revocation is not null) await revocation; }
    }

    [LinuxTheory("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    [InlineData("replace")]
    [InlineData("remove")]
    [InlineData("branding")]
    public async Task ExpiryAfterCallbackAdmissionDoesNotReorderAnAlreadyAdmittedCommit(string operation)
    {
        using var fixture = await Fixture.Create(); fixture.Barrier.PauseAtCommit = true; fixture.Barrier.Armed = true;
        var mutation = Task.Run(() => fixture.Mutate(operation));
        try
        {
            await fixture.Barrier.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            fixture.Host.Clock.Advance(TimeSpan.FromHours(12));
            fixture.Barrier.Resume.Set();
            Assert.Equal(HttpStatusCode.Redirect, (await mutation).StatusCode);
            Assert.True(fixture.Barrier.CommittedSequence > 0);
            Assert.Equal(HttpStatusCode.Forbidden, (await fixture.Player.Send($"/media/character/{fixture.CharacterId}/portrait")).StatusCode);
        }
        finally { fixture.Barrier.Resume.Set(); await mutation; }
    }

    [LinuxTheory("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    [InlineData("replace")]
    [InlineData("remove")]
    public async Task OwnershipChangedAfterPreparationIsRejectedInsideFinalTransaction(string operation)
    {
        using var fixture = await Fixture.Create(); fixture.Barrier.Armed = true;
        var mutation = Task.Run(() => fixture.Mutate(operation));
        try
        {
            await fixture.Barrier.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using (var db = fixture.Host.Factory.CreateDbContext())
            {
                var other = new PlayerProfile { Name = "New owner" };
                db.Add(other); db.SaveChanges();
                db.PlayerCharacters.Single(x => x.Id == fixture.CharacterId).PlayerProfileId = other.Id;
                db.SaveChanges(); // Preparation holds neither a write transaction nor the security gate.
            }
            fixture.Barrier.Resume.Set();
            Assert.Equal(HttpStatusCode.NotFound, (await mutation).StatusCode);
            fixture.AssertPreserved();
        }
        finally { fixture.Barrier.Resume.Set(); await mutation; }
    }

    [LinuxTheory("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    [InlineData("replace")]
    [InlineData("remove")]
    public async Task ChangedReferenceAfterPreparationCannotBeOverwrittenOrDeleted(string operation)
    {
        using var fixture = await Fixture.Create(); fixture.Barrier.Armed = true;
        var mutation = Task.Run(() => fixture.Mutate(operation));
        var changed = Guid.NewGuid();
        try
        {
            await fixture.Barrier.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using (var db = fixture.Host.Factory.CreateDbContext())
            {
                db.PresentationAssets.Single(x => x.SubjectId == fixture.CharacterId).BlobId = changed;
                db.SaveChanges();
            }
            var changedPath = Path.Combine(fixture.Store.DirectoryPath, changed.ToString("N") + ".png");
            File.WriteAllBytes(changedPath, [7,8,9]);
            fixture.PreviousBlob = changed;
            fixture.PreviousFiles[Path.GetFileName(changedPath)] = [7,8,9];
            fixture.Barrier.Resume.Set();
            Assert.Equal(HttpStatusCode.NotFound, (await mutation).StatusCode);
            fixture.AssertPreserved();
        }
        finally { fixture.Barrier.Resume.Set(); await mutation; }
    }

    [LinuxTheory("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    [InlineData("replace")]
    [InlineData("remove")]
    [InlineData("branding")]
    public async Task ContendedSqliteWriteTimesOutWithoutChangingPriorState(string operation)
    {
        using var fixture = await Fixture.Create(); fixture.Barrier.Armed = true;
        var mutation = Task.Run(() => fixture.Mutate(operation));
        try
        {
            await fixture.Barrier.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var blocker = fixture.Host.Factory.CreateDbContext();
            using var transaction = blocker.Database.BeginTransaction();
            fixture.Barrier.Resume.Set();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await mutation.WaitAsync(TimeSpan.FromSeconds(8))).StatusCode);
            transaction.Rollback();
            fixture.AssertPreserved();
        }
        finally { fixture.Barrier.Resume.Set(); await mutation; }
    }

    [LinuxTheory("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    [InlineData("replace")]
    [InlineData("remove")]
    [InlineData("branding")]
    public async Task ActualSqliteCommitFailurePreservesPriorReferencesAndFiles(string operation)
    {
        using var fixture = await Fixture.Create(); fixture.Barrier.PauseAtCommit = true; fixture.Barrier.FailCommit = true; fixture.Barrier.Armed = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await fixture.Mutate(operation)).StatusCode);
        fixture.AssertPreserved();
    }

    [LinuxFact("Requires production Linux HTTPS configuration and secure-grant directory fsync.")]
    public async Task AmbiguousPostCommitFailureNeverDeletesAReferencedCandidateOrPriorFile()
    {
        using var fixture = await Fixture.Create(); fixture.Barrier.PauseAtCommit = true; fixture.Barrier.Resume.Set();
        fixture.Barrier.FailAfterCommit = true; fixture.Barrier.Armed = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await fixture.Mutate("replace")).StatusCode);
        using var db = fixture.Host.Factory.CreateDbContext();
        var current = db.PresentationAssets.Single(x => x.SubjectId == fixture.CharacterId);
        Assert.NotEqual(fixture.PreviousBlob, current.BlobId);
        Assert.NotNull(fixture.Store.Read("character", fixture.CharacterId, "portrait"));
        Assert.True(File.Exists(Path.Combine(fixture.Store.DirectoryPath, fixture.PreviousBlob.ToString("N") + ".png")));
    }
}
