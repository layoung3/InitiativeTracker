using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace InitiativeTracker.Core.Coordination;

public static class DeviceProtocol
{
    public const int Version = 1;
    public static readonly string[] Capabilities = ["network.prepare", "network.commit", "network.recover", "network.reconnect"];
    public static string Fingerprint(X509Certificate2 certificate) => certificate.GetCertHashString(HashAlgorithmName.SHA256);
    public static bool PinMatches(X509Certificate2? certificate, string pin) => certificate is not null &&
        pin.Length == 64 && CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(Fingerprint(certificate)), System.Text.Encoding.ASCII.GetBytes(pin.ToUpperInvariant()));
    public static X509Certificate2 Certificate(string path, string name, bool server)
    {
        if (File.Exists(path)) return X509CertificateLoader.LoadPkcs12FromFile(path, null);
        using var key = RSA.Create(3072);
        var request = new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var usages = new OidCollection { new(server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
        PrivateFile.Write(path, certificate.Export(X509ContentType.Pkcs12));
        return X509CertificateLoader.LoadPkcs12FromFile(path, null);
    }
}

public sealed record WifiProfile(string Ssid, string Password, string KeyManagement = "wpa-psk");
public sealed record EnrollmentRequest(Guid DeviceId, string Name, int Version, string[] Capabilities, Guid IntentId);
public sealed record Enrollment(Guid DeviceId, string Name, string CertificatePin, Guid Generation, string[] Capabilities, DateTimeOffset RequestedUtc, bool Approved, bool Revoked = false, Guid? DisplayId = null, Guid IntentId = default);
public sealed record DeviceBinding(Guid HubId, Guid DeviceId, Guid Generation, string HubPin, string HubUrl, string DisplayUrl);
public enum TransitionStage { Preparing, Committed, HubConfirmed, Completed, Recovered, Expired }
public sealed record DeviceProgress(Guid DeviceId, Guid Generation, bool Ready = false, bool CommitReceived = false, bool Reconnected = false, bool Finalized = false, string? Failure = null, string? Name = null);
public sealed record Transition(Guid Id, long Revision, Guid HubId, string TargetKind, string? TargetUuid,
    DateTimeOffset PrepareExpiresUtc, TransitionStage Stage, DateTimeOffset? SwitchUtc,
    DateTimeOffset? DeadlineUtc, DeviceProgress[] Devices, Guid[] LeftBehind);
public sealed record TransitionPlan(int Version, Transition Transition, Guid DeviceId, Guid Generation, WifiProfile Target, WifiProfile Recovery);
public sealed record DeviceReceipt(int Version, Guid TransitionId, long Revision, Guid Generation, string Stage);
public sealed record DeviceStatus(int Version, Guid HubId, Guid Generation, TransitionPlan? Plan, string? MdnsHost = null);

public static class TransitionRules
{
    public static bool Pending(Transition? t) => t?.Stage is TransitionStage.Preparing or TransitionStage.Committed or TransitionStage.HubConfirmed;
    public static bool ValidPlan(TransitionPlan p, DeviceBinding b, long lastRevision, DateTimeOffset now) =>
        p.Version == DeviceProtocol.Version && p.DeviceId == b.DeviceId && p.Generation == b.Generation &&
        p.Transition.HubId == b.HubId && p.Transition.Id != Guid.Empty && p.Transition.Revision > lastRevision &&
        p.Transition.Stage == TransitionStage.Preparing && p.Transition.PrepareExpiresUtc > now &&
        p.Transition.PrepareExpiresUtc <= now.AddMinutes(3) &&
        p.Transition.Devices.Any(d => d.DeviceId == b.DeviceId && d.Generation == b.Generation) &&
        p.Transition.TargetKind is "wifi" or "hotspot";
    public static bool SameTransaction(Transition a, Transition b) => a.Id == b.Id && a.Revision == b.Revision && a.HubId == b.HubId;
    public static bool ValidCommit(Transition prepared, Transition committed, DateTimeOffset now) =>
        SameTransaction(prepared, committed) && committed.Stage is TransitionStage.Committed or TransitionStage.HubConfirmed &&
        prepared.TargetKind == committed.TargetKind && prepared.TargetUuid == committed.TargetUuid &&
        prepared.PrepareExpiresUtc == committed.PrepareExpiresUtc && committed.SwitchUtc is not null &&
        committed.DeadlineUtc > committed.SwitchUtc && committed.DeadlineUtc <= committed.SwitchUtc.Value.AddMinutes(3) &&
        committed.DeadlineUtc > now && committed.SwitchUtc <= now.AddSeconds(15);
}

/// <summary>One writer under the owning service's lock; protected parent directory, atomic replace and fsync.</summary>
public static class PrivateFile
{
    public static void Directory(string path)
    {
        System.IO.Directory.CreateDirectory(path);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    public static T? Read<T>(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<T>(File.ReadAllBytes(path)) ?? throw new IOException("Invalid private state") : default;
    public static void Save<T>(string path, T value) => Write(path, JsonSerializer.SerializeToUtf8Bytes(value));
    public static void Write(string path, byte[] bytes)
    {
        Directory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Unsafe state path");
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (OperatingSystem.IsLinux()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporary, options))
            { file.Write(bytes); file.Flush(true); }
            File.Move(temporary, path, true);
            if (OperatingSystem.IsLinux())
            {
                var descriptor = Native.open(Path.GetDirectoryName(path)!, 0x10000);
                if (descriptor < 0) throw new IOException("Cannot open state directory");
                try { if (Native.fsync(descriptor) != 0) throw new IOException("Cannot sync state directory"); }
                finally { Native.close(descriptor); }
            }
        }
        finally { File.Delete(temporary); }
    }
    private static class Native
    {
        [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)] internal static extern int open(string path, int flags);
        [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)] internal static extern int fsync(int fd);
        [System.Runtime.InteropServices.DllImport("libc")] internal static extern int close(int fd);
    }
}
