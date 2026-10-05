namespace InitiativeTracker.Web.Data;

public class PlayerDeviceCredential
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid PlayerProfileId { get; set; }

    /*
     * SHA-256 hash of the random browser credential.
     * The raw credential exists only in the player's HttpOnly cookie.
     */
    public string TokenHash { get; set; } =
        string.Empty;

    public string Name { get; set; } =
        "Browser";

    public DateTime CreatedAtUtc { get; set; }

    public DateTime LastSeenAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }
}
