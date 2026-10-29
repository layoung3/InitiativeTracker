using InitiativeTracker.Core.Networking;

namespace InitiativeTracker.Tests;

public class SatelliteNetworkCoordinationTests
{
    private static readonly Guid Hub = Guid.NewGuid();
    private static readonly Guid Satellite = Guid.NewGuid();

    private static SatelliteNetworkProposal Proposal(DateTimeOffset now) =>
        new(Hub, Satellite, Guid.NewGuid(), "home-network-profile", now.AddMinutes(3), 12);

    [Fact]
    public void FreshBoundProposalAccepted()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(SatelliteNetworkCoordination.IsValidProposal(Proposal(now), Hub, Satellite, 11, now));
    }

    [Fact]
    public void ExpiredOrStaleProposalRejected()
    {
        var now = DateTimeOffset.UtcNow;
        var proposal = Proposal(now);
        Assert.False(SatelliteNetworkCoordination.IsValidProposal(proposal with { ExpiresUtc = now }, Hub, Satellite, 11, now));
        Assert.False(SatelliteNetworkCoordination.IsValidProposal(proposal, Hub, Satellite, 12, now));
        Assert.False(SatelliteNetworkCoordination.IsValidProposal(proposal with { ExpiresUtc = now.AddHours(1) }, Hub, Satellite, 11, now));
    }

    [Fact]
    public void WrongSatelliteCannotPrepare()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(SatelliteNetworkCoordination.IsValidProposal(Proposal(now), Hub, Guid.NewGuid(), 11, now));
    }

    [Fact]
    public void CommitRequiresMatchingPreparedReceipt()
    {
        var proposal = Proposal(DateTimeOffset.UtcNow);
        Assert.False(SatelliteNetworkCoordination.MayCommit(proposal, null, true));
        var prepared = new SatelliteProvisioningReceipt(Hub, Satellite, proposal.TransactionId, proposal.Revision, SatelliteProvisioningStage.Prepared);
        Assert.True(SatelliteNetworkCoordination.MayCommit(proposal, prepared, true));
        Assert.False(SatelliteNetworkCoordination.MayCommit(proposal, prepared with { TransactionId = Guid.NewGuid() }, true));
        Assert.False(SatelliteNetworkCoordination.MayCommit(proposal, prepared with { Stage = SatelliteProvisioningStage.Offered }, true));
    }

    [Fact]
    public void OfflineSatelliteUsesSharedFallbackInsteadOfUnknownPassword()
    {
        Assert.Equal(SatelliteRecoveryAction.TrySharedRecoveryHotspot,
            SatelliteNetworkCoordination.ChooseRecovery(SatelliteLinkState.HubUnreachable, false, true, true, true));
        Assert.Equal(SatelliteRecoveryAction.WaitForHubRecoveryWindow,
            SatelliteNetworkCoordination.ChooseRecovery(SatelliteLinkState.HubUnreachable, false, true, true, false));
        Assert.Equal(SatelliteRecoveryAction.RequirePhysicalRecovery,
            SatelliteNetworkCoordination.ChooseRecovery(SatelliteLinkState.HubUnreachable, false, false, false, false));
    }

    [Fact]
    public void SatelliteProofBindsIdentityTransactionAndChallenge()
    {
        byte[] key = Enumerable.Repeat((byte)0xA7, 32).ToArray();
        byte[] challenge = Enumerable.Range(0, 20).Select(i => (byte)i).ToArray();
        var transaction = Guid.NewGuid();
        var proof = SatelliteProof.Create(key, Hub, Satellite, transaction, 12, "Prepared", challenge);
        Assert.True(SatelliteProof.Verify(key, Hub, Satellite, transaction, 12, "Prepared", challenge, proof));
        Assert.False(SatelliteProof.Verify(key, Hub, Guid.NewGuid(), transaction, 12, "Prepared", challenge, proof));
        Assert.False(SatelliteProof.Verify(key, Hub, Satellite, transaction, 13, "Prepared", challenge, proof));
        Assert.False(SatelliteProof.Verify(key, Hub, Satellite, transaction, 12, "Committed", challenge, proof));
        Assert.False(SatelliteProof.Verify(key, Hub, Satellite, transaction, 12, "Prepared", new byte[20], proof));
    }

    [Fact]
    public void ShortPairingKeyRejected()
    {
        Assert.Throws<ArgumentException>(() => SatelliteProof.Create(new byte[8], Hub, Satellite, Guid.NewGuid(), 1, "Prepared", new byte[20]));
    }
}
