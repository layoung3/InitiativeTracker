namespace InitiativeTracker.Web.Data;

public class DevicePairingRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string PairingCode { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string Status { get; set; } = "Pending";

    public DateTime RequestedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public Guid? ApprovedDeviceId { get; set; }
}