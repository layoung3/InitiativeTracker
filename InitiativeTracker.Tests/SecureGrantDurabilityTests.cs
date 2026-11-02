using System.Text.Json;
using InitiativeTracker.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;

namespace InitiativeTracker.Tests;

public sealed class SecureGrantDurabilityTests
{
    private sealed record Observation(SecurePersistenceSync Stage, string Directory, bool Fence, Guid? Epoch);
    private sealed class Persistence : SecureGrantPersistence
    {
        private readonly SecureAccessStorage storage;
        private readonly IDataProtectionProvider protection;
        public Persistence(SecureAccessStorage storage, IDataProtectionProvider protection) : base(storage, protection)
        { this.storage = storage; this.protection = protection; }
        public readonly List<Observation> Events = [];
        public SecurePersistenceSync? FailStage;
        public bool Unsupported;
        protected override void SynchronizeDirectory(string directory, SecurePersistenceSync stage)
        {
            var state = Path.Combine(storage.Root, "state.bin");
            Guid? epoch = null;
            if (File.Exists(state)) epoch = JsonSerializer.Deserialize<SecureGrantState>(protection.CreateProtector("InitiativeTracker.SecureAccess.State.v1").Unprotect(File.ReadAllBytes(state)))!.Epoch;
            Events.Add(new(stage, directory, File.Exists(Path.Combine(storage.Root, "mutation.pending")), epoch));
            if (Unsupported) throw new PlatformNotSupportedException();
            if (FailStage == stage) { FailStage = null; throw new IOException("Injected synchronization failure."); }
            base.SynchronizeDirectory(directory, stage); // Real Linux fsync on successful stages.
        }
    }
    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "secure-durability-" + Guid.NewGuid());
        public readonly IDataProtectionProvider Protection = new EphemeralDataProtectionProvider();
        public SecureAccessStorage Storage => new(Path.Combine(Root, "new-parent", "secure-access"), true);
        public Persistence New() => new(Storage, Protection);
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    [LinuxFact("Exercises real Linux directory fsync, durable fences and protected state across restart.")]
    public void SuccessfulCommitSynchronizesDirectoriesInOrderIncludingCreatedParents()
    {
        using var fixture = new Fixture(); var store = fixture.New(); var first = new SecureGrantState();
        store.Write(first, recovery: true);
        Assert.Equal(first.Epoch, store.Read()!.Epoch);
        Assert.Equal(SecurePersistenceSync.StoreDirectory, store.Events[0].Stage);
        var ancestors = new List<string>();
        for (var parent = new DirectoryInfo(fixture.Storage.Root).Parent; parent is not null; parent = parent.Parent) ancestors.Add(parent.FullName);
        Assert.Equal(ancestors, store.Events.Where(e => e.Stage == SecurePersistenceSync.ParentDirectory).Select(e => e.Directory));
        var commit = store.Events.Where(e => e.Stage is SecurePersistenceSync.FenceCreated or SecurePersistenceSync.StateReplaced or SecurePersistenceSync.FenceRemoved).ToArray();
        Assert.Collection(commit,
            e => { Assert.Equal(SecurePersistenceSync.FenceCreated, e.Stage); Assert.True(e.Fence); Assert.Null(e.Epoch); },
            e => { Assert.Equal(SecurePersistenceSync.StateReplaced, e.Stage); Assert.True(e.Fence); Assert.Equal(first.Epoch, e.Epoch); },
            e => { Assert.Equal(SecurePersistenceSync.FenceRemoved, e.Stage); Assert.False(e.Fence); Assert.Equal(first.Epoch, e.Epoch); });
        store.Events.Clear(); var second = new SecureGrantState(); store.Write(second);
        Assert.Equal(new[] { SecurePersistenceSync.FenceCreated, SecurePersistenceSync.StateReplaced, SecurePersistenceSync.FenceRemoved }, store.Events.Select(e => e.Stage));
        Assert.Equal(first.Epoch, store.Events[0].Epoch);
        Assert.Equal(second.Epoch, store.Events[1].Epoch);
        Assert.Equal(second.Epoch, store.Read()!.Epoch);
    }

    [LinuxTheory("Exercises real Linux directory fsync, durable fences and protected state across restart.")]
    [InlineData(SecurePersistenceSync.StoreDirectory)]
    [InlineData(SecurePersistenceSync.ParentDirectory)]
    [InlineData(SecurePersistenceSync.FenceCreated)]
    [InlineData(SecurePersistenceSync.StateReplaced)]
    [InlineData(SecurePersistenceSync.FenceRemoved)]
    public void FailedStageReturnsUnavailableAndLeavesRestartFencedUntilRecovery(SecurePersistenceSync stage)
    {
        using var fixture = new Fixture(); var store = fixture.New(); store.FailStage = stage;
        Assert.Throws<SecureAccessUnavailable>(() => store.Write(new SecureGrantState(), recovery: true));
        Assert.Null(store.Read());
        Assert.True(File.Exists(Path.Combine(fixture.Storage.Root, "mutation.pending")));
        Assert.Equal(SecurePersistenceSync.FailureFence, store.Events[^1].Stage);
        var restarted = fixture.New(); Assert.Null(restarted.Read());
        Assert.Throws<SecureAccessUnavailable>(() => restarted.Write(new SecureGrantState()));
        var recovered = new SecureGrantState(); restarted.Write(recovered, recovery: true);
        Assert.Equal(recovered.Epoch, restarted.Read()!.Epoch);
    }

    [LinuxFact("Exercises real Linux directory fsync, durable fences and protected state across restart.")]
    public void UnsupportedDirectoryDurabilityDoesNotReadOrAcknowledgeSecureState()
    {
        using var fixture = new Fixture(); var good = fixture.New(); good.Write(new SecureGrantState(), recovery: true);
        var unsupported = fixture.New(); unsupported.Unsupported = true;
        Assert.Null(unsupported.Read());
        Assert.Throws<SecureAccessUnavailable>(() => unsupported.Write(new SecureGrantState(), recovery: true));
        Assert.Null(unsupported.Read());
        Assert.Null(fixture.New().Read());
    }
}
