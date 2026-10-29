namespace InitiativeTracker.Core.Networking;

public sealed record NetworkPolicy(
    bool PreferSavedNetworks = true,
    bool AllowPrivateFallback = true,
    int ConnectionTimeoutSeconds = 45,
    int SatellitePreparationTimeoutSeconds = 30,
    int VerificationTimeoutSeconds = 45,
    int RecoveryTimeoutSeconds = 60)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!AllowPrivateFallback) errors.Add("Private hotspot fallback must remain enabled.");
        if (ConnectionTimeoutSeconds is < 10 or > 180) errors.Add("Connection timeout must be 10–180 seconds.");
        if (SatellitePreparationTimeoutSeconds is < 5 or > 180) errors.Add("Satellite preparation timeout must be 5–180 seconds.");
        if (VerificationTimeoutSeconds is < 10 or > 180) errors.Add("Verification timeout must be 10–180 seconds.");
        if (RecoveryTimeoutSeconds is < 15 or > 300) errors.Add("Recovery timeout must be 15–300 seconds.");
        return errors;
    }
}

public sealed record NetworkCandidate(string Id, int Priority, bool Available, bool Authorized);
public static class NetworkSelection
{
    // Never accepts an unknown or unauthorized network. No scan or connection side effects.
    public static string? PreferredSavedNetwork(IEnumerable<NetworkCandidate> candidates) => candidates
        .Where(c => c.Available && c.Authorized && !string.IsNullOrWhiteSpace(c.Id))
        .OrderByDescending(c => c.Priority)
        .ThenBy(c => c.Id, StringComparer.Ordinal)
        .Select(c => c.Id)
        .FirstOrDefault();

    public static NetworkMode SelectBootMode(IEnumerable<NetworkCandidate> candidates, NetworkPolicy policy)
    {
        if (policy.Validate().Count > 0) throw new ArgumentException("Invalid network policy", nameof(policy));
        return policy.PreferSavedNetworks && PreferredSavedNetwork(candidates) is not null
            ? NetworkMode.ExternalWifi : NetworkMode.PrivateHotspot;
    }
}
