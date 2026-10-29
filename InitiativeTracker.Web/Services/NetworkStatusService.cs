using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace InitiativeTracker.Web.Services;

// N1 is strictly read-only. Network changes will be added only after
// a separately tested recovery supervisor exists.
public sealed class NetworkStatusService
{
    public sealed record WifiNetwork(string Ssid, int? Signal, string Security, bool Connected);
    public sealed record SavedNetwork(string Name, string Uuid);
    public sealed record Snapshot(string InterfaceName, string Connection, string? Ipv4,
        bool IsConnected, bool ToolAvailable, IReadOnlyList<WifiNetwork> Networks, string? Message,
        IReadOnlyList<SavedNetwork> SavedNetworks, bool ScanIsCached);

    public async Task<Snapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var wifi = NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => x.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                     || x.Name.StartsWith("wl", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name).FirstOrDefault();
        var name = wifi?.Name ?? "wlan0";
        var ip = wifi?.GetIPProperties().UnicastAddresses
            .FirstOrDefault(x => x.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();

        if (!OperatingSystem.IsLinux())
            return new(name, "Unavailable", ip, false, false, [],
                "Wi-Fi information is supported on the Raspberry Pi.", [], true);

        var connectionResult = await RunNmcliAsync(
            ["-g", "GENERAL.CONNECTION", "device", "show", name], cancellationToken);
        var scanResult = await RunNmcliAsync(
            ["-t", "-f", "IN-USE,SSID,SIGNAL,SECURITY", "device", "wifi", "list", "ifname", name, "--rescan", "no"], cancellationToken);
        // Reading the existing cache must not request a fresh scan, particularly
        // while wlan0 is operating as the private access point.
        var savedResult = await RunNmcliAsync(
            ["-t", "-f", "UUID,TYPE", "connection", "show"], cancellationToken);
        var saved = new List<SavedNetwork>();
        if (savedResult.Success)
        {
            foreach (var line in savedResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = SplitEscaped(line.TrimEnd('\r'));
                if (fields.Count != 2 || !Guid.TryParse(fields[0], out _) ||
                    fields[1] is not ("wifi" or "802-11-wireless")) continue;
                // Use UUID for lookup: profile names can contain separators.
                var profileName = await RunNmcliAsync(
                    ["-g", "connection.id", "connection", "show", fields[0]], cancellationToken);
                var mode = await RunNmcliAsync(
                    ["-g", "802-11-wireless.mode", "connection", "show", fields[0]], cancellationToken);
                if (!profileName.Success || !mode.Success || mode.Output.Trim() == "ap") continue;
                saved.Add(new(profileName.Output.Trim(), fields[0]));
            }
        }
        var connectedName = connectionResult.Success ? connectionResult.Output.Trim() : "Unavailable";
        var networks = new List<WifiNetwork>();
        if (scanResult.Success)
        {
            foreach (var line in scanResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var columns = SplitEscaped(line.TrimEnd('\r'));
                if (columns.Count != 4 || string.IsNullOrWhiteSpace(columns[1])) continue;
                int? signal = int.TryParse(columns[2], out var value) ? value : null;
                networks.Add(new(columns[1], signal, columns[3], columns[0] == "*"));
            }
        }
        var distinct = networks.GroupBy(x => (x.Ssid, x.Security))
            .Select(x => x.OrderByDescending(n => n.Connected).ThenByDescending(n => n.Signal).First())
            .OrderByDescending(x => x.Connected).ThenByDescending(x => x.Signal).ToList();
        var available = connectionResult.Success && scanResult.Success;
        return new(name, connectedName, ip, !string.IsNullOrWhiteSpace(ip), available, distinct,
            available ? null : "NetworkManager status could not be read. Check that nmcli is installed and available to the web service account.",
            saved, true);
    }

    // nmcli -t escapes colons and backslashes in SSIDs.
    private static List<string> SplitEscaped(string input)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var escaped = false;
        foreach (var c in input)
        {
            if (escaped) { current.Append(c); escaped = false; }
            else if (c == '\\') escaped = true;
            else if (c == ':') { result.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        if (escaped) current.Append('\\');
        result.Add(current.ToString());
        return result;
    }

    private static async Task<(bool Success, string Output)> RunNmcliAsync(
        IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process { StartInfo = new ProcessStartInfo("nmcli")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            }};
            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            if (!process.Start()) return (false, string.Empty);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(4));
            try
            {
                var outputTask = process.StandardOutput.ReadToEndAsync(limit.Token);
                await process.WaitForExitAsync(limit.Token);
                return (process.ExitCode == 0, await outputTask);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                return (false, string.Empty);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return (false, string.Empty);
        }
    }
}
