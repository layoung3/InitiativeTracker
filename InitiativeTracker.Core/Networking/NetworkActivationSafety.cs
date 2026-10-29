namespace InitiativeTracker.Core.Networking;

/// <summary>
/// An explicit, fail-closed readiness assessment. It never performs network I/O.
/// No UI or caller should treat a simulated transition as authority to execute it.
/// </summary>
public sealed record NetworkActivationPrerequisites(
    bool OperatorApproved,
    bool PrivilegedHelperInstalled,
    bool RecoveryHotspotProfileVerified,
    bool PersistentRecoveryWatchdogVerified,
    bool PreSwitchStatePersisted,
    bool HubReachabilityProbeConfigured,
    bool CredentialsStoredSecurely,
    bool SatellitePaired,
    bool SatelliteTransportAuthenticatedAndEncrypted,
    bool SatelliteProfilePersisted,
    bool SatelliteRecoveryTested,
    bool ActiveEncounter,
    bool PhysicalRecoveryAvailable);

public sealed record NetworkActivationAssessment(
    bool MayEnableLiveSwitching,
    IReadOnlyList<string> Blockers,
    NetworkMode? IntendedTarget,
    IReadOnlyList<string> DryRunSteps);

public static class NetworkActivationSafety
{
    public static NetworkActivationAssessment Assess(
        NetworkActivationPrerequisites p,
        NetworkPolicy policy,
        NetworkMode currentMode,
        NetworkMode targetMode,
        bool satelliteRequired)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(policy);
        var blockers = new List<string>();
        blockers.AddRange(policy.Validate());
        if (!p.OperatorApproved) blockers.Add("Explicit local administrator approval is required.");
        if (!p.PrivilegedHelperInstalled) blockers.Add("Restricted privileged network helper is not installed.");
        if (!p.RecoveryHotspotProfileVerified) blockers.Add("Recovery hotspot profile is not verified.");
        if (!p.PersistentRecoveryWatchdogVerified) blockers.Add("Independent recovery watchdog is not verified.");
        if (!p.PreSwitchStatePersisted) blockers.Add("Last-known-good network state is not persisted.");
        if (!p.HubReachabilityProbeConfigured) blockers.Add("Hub reachability verification is missing.");
        if (!p.CredentialsStoredSecurely && targetMode == NetworkMode.ExternalWifi)
            blockers.Add("Destination network credentials are not securely stored.");
        if (p.ActiveEncounter) blockers.Add("Do not switch while an encounter is active.");
        if (!p.PhysicalRecoveryAvailable) blockers.Add("A tested physical/console recovery path is required for first hardware trials.");
        if (satelliteRequired)
        {
            if (!p.SatellitePaired) blockers.Add("Satellite pairing not established.");
            if (!p.SatelliteTransportAuthenticatedAndEncrypted) blockers.Add("Satellite transport is not authenticated and encrypted.");
            if (!p.SatelliteProfilePersisted) blockers.Add("Satellite has not persisted its target/recovery profiles.");
            if (!p.SatelliteRecoveryTested) blockers.Add("Satellite offline recovery has not been tested.");
        }
        var steps = new List<string>
        {
            $"DRY RUN ONLY: {currentMode} -> {targetMode}",
            "Record last-known-good mode and connection profile on persistent disk.",
            satelliteRequired ? "Deliver authenticated encrypted proposal; wait for a durable satellite prepared receipt." : "No satellite acknowledgment required.",
            "Arm independent watchdog before disconnecting from current network.",
            "Switch hub network, then verify actual IP connectivity and hub reachability.",
            "Confirm both devices on target network before disarming watchdog.",
            "On timeout/power interruption, recover previous connection or private hotspot."
        };
        return new(blockers.Count == 0, blockers, targetMode, steps);
    }
}
