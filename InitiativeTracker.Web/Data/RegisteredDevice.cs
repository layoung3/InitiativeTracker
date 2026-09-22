namespace InitiativeTracker.Web.Data;

public class RegisteredDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string DeviceType { get; set; } = "Display";

    public bool IsTrusted { get; set; } = true;

    public DateTime PairedAtUtc { get; set; }

    public DateTime? LastSeenAtUtc { get; set; }
}