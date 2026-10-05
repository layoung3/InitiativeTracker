namespace InitiativeTracker.Web.Data;

public class PlayerProfile
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public string Name { get; set; } =
        string.Empty;

    /*
     * Legacy profile-wide token.
     *
     * Existing installations may already have this value stored
     * in the InitiativeTracker.PlayerProfile cookie. New profiles
     * no longer use it as their normal credential. It remains
     * nullable temporarily so an existing browser can be upgraded
     * to a per-device credential without losing its characters.
     */
    public string? AccessToken { get; set; }

    /*
     * SHA-256 hash of the player's human-readable recovery key.
     * The plaintext recovery key is shown only when generated.
     */
    public string? RecoveryKeyHash { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? LastSeenAtUtc { get; set; }
}
