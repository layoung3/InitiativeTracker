namespace InitiativeTracker.Core.Networking;

/// <summary>Pure input validation only. Does not retain credentials or configure network devices.</summary>
public static class WifiProvisioningValidation
{
    public static IReadOnlyList<string> Validate(string? ssid, string? password, bool requiresPassword)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(ssid))
            errors.Add("Enter a Wi-Fi network name.");
        else if (System.Text.Encoding.UTF8.GetByteCount(ssid) > 32 || ssid.Any(char.IsControl))
            errors.Add("Network name must be at most 32 UTF-8 bytes and contain no control characters.");

        if (requiresPassword)
        {
            // This preparatory form supports WPA2-Personal passphrases only.
            // Future helper must detect/handle WPA3 and enterprise networks explicitly.
            if (password is null || password.Length < 8 || password.Length > 63 || password.Any(c => c < 32 || c > 126))
                errors.Add("Enter an 8–63 character printable ASCII Wi-Fi password.");
        }
        else if (!string.IsNullOrEmpty(password))
            errors.Add("Do not enter a password for an open network.");
        return errors;
    }
}
