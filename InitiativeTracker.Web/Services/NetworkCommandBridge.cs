using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace InitiativeTracker.Web.Services;

/// <summary>Restricted local Unix socket client. No sudo, shell or arbitrary commands.</summary>
public sealed class NetworkCommandBridge(IConfiguration configuration)
{
    private const string SocketPath = "/run/initiativetracker/network-bridge.sock";
    public bool Enabled => OperatingSystem.IsLinux() &&
        configuration.GetValue<bool>("Tracker:NetworkCommandsEnabled");

    /// <summary>
    /// Fetches an ephemeral Wi-Fi QR payload for the loopback-only physical Setup page.
    /// Never log, cache, or expose the returned value to remote web pages.
    /// </summary>
    public async Task<string?> GetHotspotJoinQrAsync(CancellationToken cancellationToken)
    {
        if (!Enabled) return null;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), deadline.Token);
            using var stream = new NetworkStream(socket, ownsSocket: false);
            await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"action\":\"hotspot-join-qr\",\"uuid\":null}\n"), deadline.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var reply = await reader.ReadLineAsync(deadline.Token);
            if (reply is null || reply.Length > 1024) return null;
            using var doc = JsonDocument.Parse(reply);
            if (!doc.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True ||
                !doc.RootElement.TryGetProperty("wifiQr", out var data) || data.ValueKind != JsonValueKind.String)
                return null;
            var payload = data.GetString();
            return payload is { Length: > 0 and < 400 } && payload.StartsWith("WIFI:T:WPA;", StringComparison.Ordinal) ? payload : null;
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public async Task<(bool Success, string Message)> ProvisionAsync(
        string ssid, string password, CancellationToken cancellationToken)
    {
        if (!Enabled) return (false, "Network commands are disabled by the Pi administrator.");
        if (string.IsNullOrWhiteSpace(ssid) || Encoding.UTF8.GetByteCount(ssid) > 32 ||
            string.IsNullOrEmpty(password) || Encoding.UTF8.GetByteCount(password) is < 8 or > 63)
            return (false, "Invalid Wi-Fi name or WPA password.");
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), deadline.Token);
            using var stream = new NetworkStream(socket, ownsSocket: false);
            var request = JsonSerializer.Serialize(new { action = "provision-wifi", uuid = new { ssid, password } });
            await stream.WriteAsync(Encoding.UTF8.GetBytes(request + "\n"), deadline.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var reply = await reader.ReadLineAsync(deadline.Token);
            if (reply is null || reply.Length > 1024) return (false, "Invalid provisioning response.");
            using var doc = JsonDocument.Parse(reply);
            if (!doc.RootElement.TryGetProperty("ok", out var ok) ||
                ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return (false, "Invalid provisioning response.");
            return ok.GetBoolean()
                ? (true, "Wi-Fi connection saved. If you are on the private hotspot, select the new saved connection below to switch with rollback protection.")
                : (false, "Wi-Fi provisioning failed. Check the root bridge journal (credentials are not logged).");
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            return (false, "Secure network bridge unavailable or timed out.");
        }
    }

    public async Task<(bool Success, string Message)> RotateHotspotPasswordAsync(
        string password, CancellationToken cancellationToken)
    {
        if (!Enabled) return (false, "Network commands are disabled by the Pi administrator.");
        if (string.IsNullOrEmpty(password) || Encoding.UTF8.GetByteCount(password) is < 8 or > 63 ||
            password.Any(ch => ch < 32 || ch > 126))
            return (false, "Use 8–63 printable ASCII characters for the hotspot password.");
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), deadline.Token);
            using var stream = new NetworkStream(socket, ownsSocket: false);
            var request = JsonSerializer.Serialize(new { action = "rotate-hotspot-password", uuid = new { password } });
            await stream.WriteAsync(Encoding.UTF8.GetBytes(request + "\n"), deadline.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var reply = await reader.ReadLineAsync(deadline.Token);
            if (reply is null || reply.Length > 1024) return (false, "Invalid network bridge response.");
            using var doc = JsonDocument.Parse(reply);
            if (!doc.RootElement.TryGetProperty("ok", out var ok) ||
                ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return (false, "Invalid network bridge response.");
            return ok.GetBoolean()
                ? (true, "Private hotspot password updated. The new password and physical Wi-Fi QR will apply on the next hotspot activation.")
                : (false, "Password update refused or failed. The hotspot must be inactive, with no pending network transition. Check the root broker journal.");
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            return (false, "Secure network bridge unavailable or timed out.");
        }
    }

    public async Task<(bool Success, string Message)> RunAsync(
        string action, string? uuid, CancellationToken cancellationToken)
    {
        if (!Enabled) return (false, "Network commands are not enabled for this deployment.");
        if (action is not ("status" or "begin-return" or "confirm-return" or "begin-hotspot" or "confirm-hotspot" or "provision-wifi"))
            return (false, "Unsupported network action.");
        if (action == "begin-return" && !Guid.TryParseExact(uuid, "D", out _))
            return (false, "Choose a valid saved Wi-Fi connection.");
        if (action != "begin-return" && action != "provision-wifi" && uuid is not null)
            return (false, "Unexpected connection identifier.");
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), deadline.Token);
            using var stream = new NetworkStream(socket, ownsSocket: false);
            var request = JsonSerializer.Serialize(new { action, uuid });
            await stream.WriteAsync(Encoding.UTF8.GetBytes(request + "\n"), deadline.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var reply = await reader.ReadLineAsync(deadline.Token);
            if (reply is null || reply.Length > 1024) return (false, "Invalid network bridge response.");
            using var doc = JsonDocument.Parse(reply);
            if (!doc.RootElement.TryGetProperty("ok", out var ok) ||
                ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return (false, "Invalid network bridge response.");
            return ok.GetBoolean()
                ? (true, action == "begin-return"
                    ? "Switch requested. Join the saved Wi-Fi and confirm within 3 minutes; otherwise the hotspot should be restored."
                    : action == "begin-hotspot"
                    ? "Private Wi-Fi switch requested. Join the InitiativeTracker hotspot and confirm within 3 minutes; otherwise the previous Wi-Fi will be attempted."
                    : "Network operation accepted.")
                : (false, "Network command was refused. Check the Pi bridge service journal.");
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            return (false, "Network bridge is unavailable or timed out.");
        }
    }
}
