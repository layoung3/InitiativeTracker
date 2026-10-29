namespace InitiativeTracker.Core.Networking;

// Pure decision logic: no network commands, privileges, or persistence.
public enum NetworkMode { ExternalWifi, PrivateHotspot }
public enum NetworkTransitionPhase { Idle, Preparing, AwaitingSatellite, Switching, Verifying, Recovering, Succeeded, Failed }
public enum NetworkEvent { Begin, SatellitePrepared, SatelliteUnavailable, SwitchApplied, ConnectivityConfirmed, Timeout, ConnectivityLost, RecoveryConfirmed, RecoveryFailed }
public enum NetworkAction { None, PrepareSatellite, ApplyTarget, VerifyTarget, RestorePrevious, StartPrivateHotspot, Complete, ReportFailure }

public sealed record NetworkTransitionState(
    NetworkMode PreviousMode,
    NetworkMode TargetMode,
    NetworkTransitionPhase Phase,
    bool SatelliteRequired,
    bool SatellitePrepared,
    bool RecoveryAttempted);

public sealed record NetworkDecision(NetworkTransitionState State, NetworkAction Action);

public static class NetworkTransition
{
    public static NetworkTransitionState Create(NetworkMode previous, NetworkMode target, bool satelliteRequired) =>
        new(previous, target, NetworkTransitionPhase.Idle, satelliteRequired, false, false);

    public static NetworkDecision Advance(NetworkTransitionState state, NetworkEvent stimulus)
    {
        var phase = state.Phase;
        if (phase is NetworkTransitionPhase.Succeeded or NetworkTransitionPhase.Failed)
            return new(state, NetworkAction.None);

        if (phase == NetworkTransitionPhase.Idle && stimulus == NetworkEvent.Begin)
        {
            if (state.PreviousMode == state.TargetMode)
                return new(state with { Phase = NetworkTransitionPhase.Succeeded }, NetworkAction.Complete);
            return state.SatelliteRequired
                ? new(state with { Phase = NetworkTransitionPhase.AwaitingSatellite }, NetworkAction.PrepareSatellite)
                : new(state with { Phase = NetworkTransitionPhase.Switching }, NetworkAction.ApplyTarget);
        }

        if (phase == NetworkTransitionPhase.AwaitingSatellite && stimulus == NetworkEvent.SatellitePrepared)
            return new(state with { Phase = NetworkTransitionPhase.Switching, SatellitePrepared = true }, NetworkAction.ApplyTarget);

        if (phase == NetworkTransitionPhase.Switching && stimulus == NetworkEvent.SwitchApplied)
            return new(state with { Phase = NetworkTransitionPhase.Verifying }, NetworkAction.VerifyTarget);

        if (phase == NetworkTransitionPhase.Verifying && stimulus == NetworkEvent.ConnectivityConfirmed)
            return new(state with { Phase = NetworkTransitionPhase.Succeeded }, NetworkAction.Complete);

        if (phase == NetworkTransitionPhase.Recovering && stimulus == NetworkEvent.RecoveryConfirmed)
            return new(state with { Phase = NetworkTransitionPhase.Failed }, NetworkAction.ReportFailure);

        if (phase == NetworkTransitionPhase.Recovering && stimulus == NetworkEvent.RecoveryFailed)
            return new(state with { Phase = NetworkTransitionPhase.Failed }, NetworkAction.ReportFailure);

        if (stimulus is NetworkEvent.Timeout or NetworkEvent.ConnectivityLost or NetworkEvent.SatelliteUnavailable)
        {
            if (phase == NetworkTransitionPhase.Recovering)
                return new(state with { Phase = NetworkTransitionPhase.Failed }, NetworkAction.ReportFailure);
            if (phase is NetworkTransitionPhase.AwaitingSatellite or NetworkTransitionPhase.Switching or NetworkTransitionPhase.Verifying)
                return new(state with { Phase = NetworkTransitionPhase.Recovering, RecoveryAttempted = true },
                    state.PreviousMode == NetworkMode.PrivateHotspot ? NetworkAction.StartPrivateHotspot : NetworkAction.RestorePrevious);
        }
        return new(state, NetworkAction.None);
    }
}
