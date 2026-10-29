using InitiativeTracker.Core.Networking;

namespace InitiativeTracker.Tests;

public class NetworkActivationSafetyTests
{
    private static NetworkActivationPrerequisites Ready() => new(
        OperatorApproved: true, PrivilegedHelperInstalled: true,
        RecoveryHotspotProfileVerified: true, PersistentRecoveryWatchdogVerified: true,
        PreSwitchStatePersisted: true, HubReachabilityProbeConfigured: true,
        CredentialsStoredSecurely: true, SatellitePaired: true,
        SatelliteTransportAuthenticatedAndEncrypted: true, SatelliteProfilePersisted: true,
        SatelliteRecoveryTested: true, ActiveEncounter: false, PhysicalRecoveryAvailable: true);

    private static NetworkActivationAssessment Check(NetworkActivationPrerequisites p,
        bool satelliteRequired = true, NetworkMode target = NetworkMode.ExternalWifi,
        NetworkPolicy? policy = null) => NetworkActivationSafety.Assess(
            p, policy ?? new NetworkPolicy(), NetworkMode.PrivateHotspot, target, satelliteRequired);

    [Fact] public void DefaultOrUnimplementedEnvironmentBlocksSwitch()
    {
        var denied = Check(new NetworkActivationPrerequisites(false,false,false,false,false,false,false,false,false,false,false,false,false));
        Assert.False(denied.MayEnableLiveSwitching);
        Assert.True(denied.Blockers.Count >= 8);
        Assert.Contains(denied.DryRunSteps, s => s.Contains("DRY RUN ONLY"));
    }

    [Fact] public void ReadyEnvironmentCouldBeApprovedByFutureOrchestrator()
        => Assert.True(Check(Ready()).MayEnableLiveSwitching);

    [Fact] public void ActiveEncounterBlocksSwitch()
        => Assert.Contains(Check(Ready() with {ActiveEncounter = true}).Blockers,
            s => s.Contains("encounter"));

    [Fact] public void SatelliteOfflineWithoutPersistentRecoveryBlocksSwitch()
        => Assert.Contains(Check(Ready() with {SatelliteRecoveryTested = false}).Blockers,
            s => s.Contains("offline recovery"));

    [Fact] public void MissingEncryptedSatelliteTransportBlocksSwitch()
        => Assert.Contains(Check(Ready() with {SatelliteTransportAuthenticatedAndEncrypted = false}).Blockers,
            s => s.Contains("authenticated and encrypted"));

    [Fact] public void MissingWatchdogBlocksEvenWithoutSatellite()
        => Assert.Contains(Check(Ready() with {PersistentRecoveryWatchdogVerified = false}, false).Blockers,
            s => s.Contains("watchdog"));

    [Fact] public void InvalidPolicyBlocksTransition()
        => Assert.False(Check(Ready(), policy: new NetworkPolicy(AllowPrivateFallback: false)).MayEnableLiveSwitching);

    [Fact] public void PrivateHotspotRecoveryDoesNotRequireExternalWifiCredentials()
        => Assert.True(Check(Ready() with {CredentialsStoredSecurely = false}, false, NetworkMode.PrivateHotspot).MayEnableLiveSwitching);

    [Fact] public void ExternalWifiRequiresSecureCredentials()
        => Assert.Contains(Check(Ready() with {CredentialsStoredSecurely = false}, false).Blockers,
            s => s.Contains("credentials"));

    [Fact] public void FirstHardwareTestRequiresPhysicalRecovery()
        => Assert.False(Check(Ready() with {PhysicalRecoveryAvailable = false}).MayEnableLiveSwitching);
}
