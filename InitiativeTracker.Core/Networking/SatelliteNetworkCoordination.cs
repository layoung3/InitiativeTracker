using System.Security.Cryptography;
using System.Text;

namespace InitiativeTracker.Core.Networking;

/// <summary>
/// No I/O or Wi-Fi effects. Non-secret coordination metadata only.
/// Caller must supply authenticated, encrypted transport and persist transaction state
/// before enabling real changes.
/// </summary>
public enum SatelliteLinkState { ConnectedToHub, HubUnreachable, Unknown }
public enum SatelliteProvisioningStage { Offered, Prepared, Committed, Confirmed, Aborted }
public enum SatelliteRecoveryAction { DiscoverOnCurrentNetwork, TrySharedRecoveryHotspot, WaitForHubRecoveryWindow, RequirePhysicalRecovery }

public sealed record SatelliteNetworkProposal(
    Guid HubId,
    Guid SatelliteId,
    Guid TransactionId,
    string TargetNetworkId,
    DateTimeOffset ExpiresUtc,
    long Revision);

public sealed record SatelliteProvisioningReceipt(
    Guid HubId,
    Guid SatelliteId,
    Guid TransactionId,
    long Revision,
    SatelliteProvisioningStage Stage);

public static class SatelliteNetworkCoordination
{
    public static bool IsValidProposal(SatelliteNetworkProposal? proposal, Guid expectedHubId,
        Guid expectedSatelliteId, long lastCommittedRevision, DateTimeOffset nowUtc)
        => proposal is not null
            && proposal.HubId != Guid.Empty
            && proposal.HubId == expectedHubId
            && proposal.SatelliteId != Guid.Empty
            && proposal.SatelliteId == expectedSatelliteId
            && proposal.TransactionId != Guid.Empty
            && !string.IsNullOrWhiteSpace(proposal.TargetNetworkId)
            && proposal.TargetNetworkId.Length <= 128
            && proposal.Revision > lastCommittedRevision
            && proposal.ExpiresUtc > nowUtc
            && proposal.ExpiresUtc <= nowUtc.AddMinutes(10);

    public static bool MatchesPreparedReceipt(SatelliteNetworkProposal proposal,
        SatelliteProvisioningReceipt? receipt)
        => receipt is not null
            && receipt.HubId == proposal.HubId
            && receipt.SatelliteId == proposal.SatelliteId
            && receipt.TransactionId == proposal.TransactionId
            && receipt.Revision == proposal.Revision
            && receipt.Stage == SatelliteProvisioningStage.Prepared;

    /// <summary>
    /// Select recovery behavior without guessing credentials for an unseen network.
    /// If the hub moves while the satellite is off, it can only recover through
    /// an already-shared network or an advertised recovery window.
    /// </summary>
    public static SatelliteRecoveryAction ChooseRecovery(
        SatelliteLinkState link,
        bool currentNetworkCanReachHub,
        bool recoveryHotspotProfileSaved,
        bool hubRecoveryWindowAdvertised,
        bool sharedRecoveryAvailable)
    {
        if (link == SatelliteLinkState.ConnectedToHub && currentNetworkCanReachHub)
            return SatelliteRecoveryAction.DiscoverOnCurrentNetwork;
        if (recoveryHotspotProfileSaved && sharedRecoveryAvailable)
            return SatelliteRecoveryAction.TrySharedRecoveryHotspot;
        if (recoveryHotspotProfileSaved && hubRecoveryWindowAdvertised)
            return SatelliteRecoveryAction.WaitForHubRecoveryWindow;
        return SatelliteRecoveryAction.RequirePhysicalRecovery;
    }

    public static bool MayCommit(SatelliteNetworkProposal proposal,
        SatelliteProvisioningReceipt? preparedReceipt, bool satelliteRequired)
        => !satelliteRequired || MatchesPreparedReceipt(proposal, preparedReceipt);
}

/// <summary>
/// Proof primitive for a future authenticated transport; NOT a complete pairing,
/// enrollment, replay-protection, or encrypted provisioning protocol.
/// A high-entropy, out-of-band established key is required for each paired satellite.
/// Never send a Wi-Fi password as part of the proof payload.
/// </summary>
public static class SatelliteProof
{
    public static byte[] Create(ReadOnlySpan<byte> pairingKey, Guid hubId, Guid satelliteId,
        Guid transactionId, long revision, string stage, ReadOnlySpan<byte> challenge)
    {
        Validate(pairingKey, stage, challenge);
        var message = BuildMessage(hubId, satelliteId, transactionId, revision, stage, challenge);
        return HMACSHA256.HashData(pairingKey, message);
    }

    public static bool Verify(ReadOnlySpan<byte> pairingKey, Guid hubId, Guid satelliteId,
        Guid transactionId, long revision, string stage, ReadOnlySpan<byte> challenge,
        ReadOnlySpan<byte> receivedProof)
    {
        if (receivedProof.Length != 32) return false;
        var expected = Create(pairingKey, hubId, satelliteId, transactionId, revision, stage, challenge);
        return CryptographicOperations.FixedTimeEquals(expected, receivedProof);
    }

    private static void Validate(ReadOnlySpan<byte> key, string stage, ReadOnlySpan<byte> challenge)
    {
        if (key.Length < 32) throw new ArgumentException("Pairing key must contain at least 256 bits.", nameof(key));
        if (string.IsNullOrWhiteSpace(stage) || stage.Length > 32 || !stage.All(char.IsAsciiLetter))
            throw new ArgumentException("Invalid protocol stage.", nameof(stage));
        if (challenge.Length < 16 || challenge.Length > 64)
            throw new ArgumentException("Challenge must be 16–64 bytes.", nameof(challenge));
    }

    private static byte[] BuildMessage(Guid hubId, Guid satelliteId, Guid transactionId,
        long revision, string stage, ReadOnlySpan<byte> challenge)
    {
        var header = Encoding.UTF8.GetBytes(
            $"InitiativeTracker.Satellite.v1|{hubId:N}|{satelliteId:N}|{transactionId:N}|{revision}|{stage}|");
        var bytes = new byte[header.Length + challenge.Length];
        header.CopyTo(bytes, 0);
        challenge.CopyTo(bytes.AsSpan(header.Length));
        return bytes;
    }
}
