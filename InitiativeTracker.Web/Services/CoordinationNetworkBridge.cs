using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using InitiativeTracker.Core.Coordination;

namespace InitiativeTracker.Web.Services;

public sealed class CoordinationNetworkBridge(NetworkCommandBridge commands) : ICoordinationNetwork
{
    private static async Task<JsonElement> Request(object payload, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint("/run/initiativetracker/network-bridge.sock"), timeout.Token);
        using var stream = new NetworkStream(socket);
        await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(payload), timeout.Token);
        await stream.WriteAsync("\n"u8.ToArray(), timeout.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var line = await reader.ReadLineAsync(timeout.Token);
        if (line is null || line.Length > 8192) throw new IOException("Invalid local network response");
        using var document = JsonDocument.Parse(line);
        if (!document.RootElement.GetProperty("ok").GetBoolean()) throw new IOException("Local network operation refused");
        return document.RootElement.Clone();
    }
    public async Task<(WifiProfile Target, WifiProfile Recovery)> ProfilesAsync(string kind, string? uuid, CancellationToken ct)
    {
        if (!commands.Enabled) throw new InvalidOperationException("Network commands disabled");
        var reply = await Request(new { action = "coordination-profiles", uuid = kind == "hotspot" ? null : uuid }, ct);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return (reply.GetProperty("target").Deserialize<WifiProfile>(options)!, reply.GetProperty("recovery").Deserialize<WifiProfile>(options)!);
    }
    public async Task<bool> ScheduleAsync(Transition transition, CancellationToken ct)
    {
        if (!commands.Enabled) return false;
        await Request(new { action = "coordination-schedule", uuid = new { id = transition.Id, kind = transition.TargetKind,
            target = transition.TargetUuid, switchAt = transition.SwitchUtc!.Value.ToUnixTimeSeconds(), deadline = transition.DeadlineUtc!.Value.ToUnixTimeSeconds() } }, ct);
        return true;
    }
    public async Task<bool> ConfirmAsync(Transition transition, CancellationToken ct)
    {
        if (!commands.Enabled) return false;
        await Request(new { action = "coordination-confirm", uuid = transition.Id.ToString() }, ct);
        return true;
    }
}
