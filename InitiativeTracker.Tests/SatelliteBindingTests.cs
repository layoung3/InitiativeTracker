using System.Text.Json;
using InitiativeTracker.Core.Coordination;
using InitiativeTracker.Satellite;
using InitiativeTracker.Tests.Fixtures;

namespace InitiativeTracker.Tests;

public sealed class SatelliteBindingTests
{
    [Theory]
    [InlineData("before")]
    [InlineData("status")]
    [InlineData("recovery")]
    public async Task ExpiredUncommittedFinishCannotReplaceTrustBeforeOrDuringAwait(string boundary)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pair-expiry-" + Guid.NewGuid());
        try
        {
            await using var broker = new NativeSatelliteBroker(Path.Combine(directory, "agent"));
            await using var hub = await SyntheticCoordinationHub.Start(Path.Combine(directory, "hub"));
            var clock = new PairingClock();
            await using var agent = new SatelliteAgent(broker.Configuration(), clock);
            await agent.RequestPairing(hub.Origin, hub.Pin, "Synthetic satellite", default);
            hub.Approve(agent);
            if (boundary == "before") clock.Advance(TimeSpan.FromMinutes(11));
            else hub.BeforeResponse = path =>
            {
                if (path.EndsWith("/" + boundary, StringComparison.Ordinal)) clock.Advance(TimeSpan.FromMinutes(11));
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() => agent.FinishPairing(default));
            Assert.Null(agent.Snapshot.Binding);
            Assert.Null(agent.Snapshot.PendingBinding);
            Assert.Equal(0, broker.RebindRequests);
            Assert.Equal(JsonValueKind.Null, (await broker.Run(new { action = "status" })).GetProperty("state").GetProperty("generation").ValueKind);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("lost-reply")]
    [InlineData("save-failure")]
    public async Task AlreadyCommittedBindingReconcilesAfterRestartAndExpiryWithoutDestinationReachability(string fault)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pair-restart-" + Guid.NewGuid());
        try
        {
            await using var broker = new NativeSatelliteBroker(Path.Combine(directory, "agent"));
            var hub = await SyntheticCoordinationHub.Start(Path.Combine(directory, "hub"));
            var clock = new PairingClock(); var config = broker.Configuration();
            var agent = new SatelliteAgent(config, clock);
            await agent.RequestPairing(hub.Origin, hub.Pin, "Synthetic satellite", default); hub.Approve(agent);
            var generation = agent.Snapshot.Candidate!.Generation;
            broker.DropNextRebindReply = fault == "lost-reply"; broker.FailAgentSaveAfterRebind = fault == "save-failure";
            await Assert.ThrowsAnyAsync<IOException>(() => agent.FinishPairing(default));
            Assert.NotNull(agent.Snapshot.PendingBinding);
            Assert.Null(agent.Snapshot.Binding);
            Assert.Equal(generation, (await broker.Run(new { action = "status" })).GetProperty("state").GetProperty("generation").GetGuid());
            await agent.DisposeAsync(); await hub.DisposeAsync();
            if (fault == "save-failure") broker.RestoreInterruptedAgentFile();
            clock.Advance(TimeSpan.FromMinutes(11));
            await using var restarted = new SatelliteAgent(config, clock);
            await restarted.Tick(default);
            Assert.Equal(generation, restarted.Snapshot.Binding!.Generation);
            Assert.Null(restarted.Snapshot.PendingBinding); Assert.Null(restarted.Snapshot.Candidate);
            Assert.Equal(1, broker.RebindRequests);
            Assert.Contains("/display", File.ReadAllText(Path.Combine(broker.DirectoryPath, "kiosk.json")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task UncommittedPendingIntentExpiresAfterRestartAndNeverUsesHistoricalApproval()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pair-uncommitted-" + Guid.NewGuid());
        try
        {
            await using var broker = new NativeSatelliteBroker(Path.Combine(directory, "agent"));
            await using var hub = await SyntheticCoordinationHub.Start(Path.Combine(directory, "hub"));
            var clock = new PairingClock(); var config = broker.Configuration();
            var agent = new SatelliteAgent(config, clock);
            await agent.RequestPairing(hub.Origin, hub.Pin, "Synthetic satellite", default); hub.Approve(agent);
            broker.RejectNextRebind = true;
            await Assert.ThrowsAsync<IOException>(() => agent.FinishPairing(default));
            Assert.NotNull(agent.Snapshot.PendingBinding); await agent.DisposeAsync();
            clock.Advance(TimeSpan.FromMinutes(11));
            await using var restarted = new SatelliteAgent(config, clock); await restarted.Tick(default);
            Assert.Null(restarted.Snapshot.Binding); Assert.Null(restarted.Snapshot.Candidate); Assert.Null(restarted.Snapshot.PendingBinding);
            Assert.Equal(1, broker.RebindRequests);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task OwnerForgetIntentSurvivesLostRootReplyAndCannotResurrectPendingReassignment()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pair-forget-" + Guid.NewGuid());
        try
        {
            await using var broker = new NativeSatelliteBroker(Path.Combine(directory, "agent"));
            await using var hub = await SyntheticCoordinationHub.Start(Path.Combine(directory, "hub"));
            var config = broker.Configuration(); var agent = new SatelliteAgent(config);
            await agent.RequestPairing(hub.Origin, hub.Pin, "Synthetic satellite", default); hub.Approve(agent);
            broker.DropNextRebindReply = true;
            await Assert.ThrowsAsync<IOException>(() => agent.FinishPairing(default));
            broker.DropNextResetReply = true;
            await Assert.ThrowsAsync<IOException>(() => agent.Forget(default));
            Assert.True(agent.Snapshot.ForgetRequested); await agent.DisposeAsync();
            await using var restarted = new SatelliteAgent(config); await restarted.Tick(default);
            Assert.False(restarted.Snapshot.ForgetRequested); Assert.Null(restarted.Snapshot.Binding);
            Assert.Null(restarted.Snapshot.Candidate); Assert.Null(restarted.Snapshot.PendingBinding);
            Assert.Equal(1, broker.RebindRequests);
            Assert.Equal(JsonValueKind.Null, (await broker.Run(new { action = "status" })).GetProperty("state").GetProperty("generation").ValueKind);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task MismatchedPendingIntentFailsClosedRatherThanTrustingCommittedGenerationAlone()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pair-stale-" + Guid.NewGuid());
        try
        {
            await using var broker = new NativeSatelliteBroker(Path.Combine(directory, "agent"));
            await using var hub = await SyntheticCoordinationHub.Start(Path.Combine(directory, "hub"));
            var config = broker.Configuration(); var agent = new SatelliteAgent(config);
            await agent.RequestPairing(hub.Origin, hub.Pin, "Synthetic satellite", default); hub.Approve(agent);
            broker.DropNextRebindReply = true; await Assert.ThrowsAsync<IOException>(() => agent.FinishPairing(default));
            PrivateFile.Save(Path.Combine(broker.DirectoryPath, "agent.json"), agent.Snapshot with { Candidate = agent.Snapshot.Candidate! with { IntentId = Guid.NewGuid() } });
            await agent.DisposeAsync();
            await using var restarted = new SatelliteAgent(config);
            await Assert.ThrowsAsync<IOException>(() => restarted.FinishPairing(default));
            Assert.Null(restarted.Snapshot.Binding); Assert.Equal(1, broker.RebindRequests);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
