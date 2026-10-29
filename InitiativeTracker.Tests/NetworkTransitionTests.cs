using InitiativeTracker.Core.Networking;

namespace InitiativeTracker.Tests;

public class NetworkTransitionTests
{
    [Fact]
    public void Boot_ChoosesHighestPriorityAuthorizedAvailableNetwork()
    {
        var candidates = new[] { new NetworkCandidate("guest", 99, true, false),
            new NetworkCandidate("home", 50, true, true), new NetworkCandidate("other", 40, true, true) };
        Assert.Equal("home", NetworkSelection.PreferredSavedNetwork(candidates));
        Assert.Equal(NetworkMode.ExternalWifi, NetworkSelection.SelectBootMode(candidates, new()));
    }

    [Fact]
    public void Boot_FallsBackWhenNoKnownNetworksAreAvailable() =>
        Assert.Equal(NetworkMode.PrivateHotspot, NetworkSelection.SelectBootMode([], new()));

    [Fact]
    public void UnsafePolicyIsRejected()
    {
        Assert.NotEmpty(new NetworkPolicy(AllowPrivateFallback: false).Validate());
        Assert.Throws<ArgumentException>(() => NetworkSelection.SelectBootMode([], new(AllowPrivateFallback: false)));
    }

    [Fact]
    public void SatelliteMustAcknowledgeBeforeSwitch()
    {
        var s = NetworkTransition.Create(NetworkMode.PrivateHotspot, NetworkMode.ExternalWifi, true);
        var start = NetworkTransition.Advance(s, NetworkEvent.Begin);
        Assert.Equal(NetworkAction.PrepareSatellite, start.Action);
        Assert.Equal(NetworkTransitionPhase.AwaitingSatellite, start.State.Phase);
        Assert.Equal(NetworkAction.None, NetworkTransition.Advance(start.State, NetworkEvent.SwitchApplied).Action);
        var ack = NetworkTransition.Advance(start.State, NetworkEvent.SatellitePrepared);
        Assert.Equal(NetworkAction.ApplyTarget, ack.Action);
        Assert.True(ack.State.SatellitePrepared);
    }

    [Fact]
    public void FailedExternalSwitchRecoversToPrivateHotspot()
    {
        var s = NetworkTransition.Create(NetworkMode.PrivateHotspot, NetworkMode.ExternalWifi, false);
        s = NetworkTransition.Advance(s, NetworkEvent.Begin).State;
        s = NetworkTransition.Advance(s, NetworkEvent.SwitchApplied).State;
        var failed = NetworkTransition.Advance(s, NetworkEvent.Timeout);
        Assert.Equal(NetworkAction.StartPrivateHotspot, failed.Action);
        Assert.Equal(NetworkTransitionPhase.Recovering, failed.State.Phase);
        Assert.Equal(NetworkTransitionPhase.Failed,
            NetworkTransition.Advance(failed.State, NetworkEvent.RecoveryConfirmed).State.Phase);
    }

    [Fact]
    public void FailureWhenPreviouslyOnExternalRestoresPreviousConnection()
    {
        var s = NetworkTransition.Create(NetworkMode.ExternalWifi, NetworkMode.PrivateHotspot, false);
        s = NetworkTransition.Advance(s, NetworkEvent.Begin).State;
        Assert.Equal(NetworkAction.RestorePrevious, NetworkTransition.Advance(s, NetworkEvent.Timeout).Action);
    }

    [Fact]
    public void SuccessfulSwitchEndsOnlyAfterVerification()
    {
        var s = NetworkTransition.Create(NetworkMode.ExternalWifi, NetworkMode.PrivateHotspot, false);
        s = NetworkTransition.Advance(s, NetworkEvent.Begin).State;
        s = NetworkTransition.Advance(s, NetworkEvent.SwitchApplied).State;
        Assert.Equal(NetworkTransitionPhase.Verifying, s.Phase);
        Assert.Equal(NetworkTransitionPhase.Succeeded,
            NetworkTransition.Advance(s, NetworkEvent.ConnectivityConfirmed).State.Phase);
    }

    [Fact]
    public void IdempotentModeRequestDoesNotSwitch()
    {
        var s = NetworkTransition.Create(NetworkMode.PrivateHotspot, NetworkMode.PrivateHotspot, true);
        Assert.Equal(NetworkAction.Complete, NetworkTransition.Advance(s, NetworkEvent.Begin).Action);
    }
}
