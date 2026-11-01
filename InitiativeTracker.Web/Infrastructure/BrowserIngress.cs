using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace InitiativeTracker.Web.Infrastructure;

// Created once from the accepted socket before TLS/HTTP. No request header can
// construct, update or supply this feature. Store immutable address strings.
internal sealed class BrowserIngress
{
    public string LocalAddress { get; }
    public string PeerAddress { get; }
    public int LocalPort { get; }
    public string? OriginalAddress { get; }
    public int? OriginalPort { get; }
    private BrowserIngress(IPEndPoint local, IPEndPoint peer, IPEndPoint? original)
    {
        LocalAddress = Normalize(local.Address).ToString(); PeerAddress = Normalize(peer.Address).ToString();
        LocalPort = local.Port; OriginalAddress = original?.Address.ToString(); OriginalPort = original?.Port;
    }
    private static IPAddress Normalize(IPAddress value) => value.IsIPv4MappedToIPv6 ? value.MapToIPv4() : value;
    internal static bool PrivateOrLoopback(IPAddress value)
    {
        if (IPAddress.IsLoopback(value)) return true;
        var bytes = value.GetAddressBytes();
        return value.AddressFamily == AddressFamily.InterNetwork
            ? bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168
            : value.AddressFamily == AddressFamily.InterNetworkV6 && value.ScopeId == 0 && (bytes[0] & 0xfe) == 0xfc;
    }
    internal static IPEndPoint? ParseOriginal(ReadOnlySpan<byte> value, AddressFamily family)
    {
        var ipv4 = family == AddressFamily.InterNetwork;
        if ((!ipv4 && family != AddressFamily.InterNetworkV6) || value.Length != (ipv4 ? 16 : 28) ||
            BitConverter.ToUInt16(value[..2]) != (ipv4 ? 2 : 10)) return null; // Linux native sa_family_t.
        var port = BinaryPrimitives.ReadUInt16BigEndian(value.Slice(2, 2));
        var address = new IPAddress(value.Slice(ipv4 ? 4 : 8, ipv4 ? 4 : 16));
        if (port == 0 || address.IsIPv4MappedToIPv6 || !PrivateOrLoopback(address) ||
            !ipv4 && BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(24, 4)) != 0) return null;
        return new(address, port);
    }
    internal static BrowserIngress? Capture(Socket? socket, Func<Socket, int, int, byte[], int>? read = null)
    {
        if (!OperatingSystem.IsLinux() || socket?.LocalEndPoint is not IPEndPoint local || socket.RemoteEndPoint is not IPEndPoint peer) return null;
        var family = Normalize(local.Address).AddressFamily;
        IPEndPoint? original = null;
        try
        {
            // Linux getorigdst copies exactly sockaddr_in/in6 but does not update
            // optlen. Supply the exact family size, never a larger shared buffer.
            var bytes = new byte[family == AddressFamily.InterNetwork ? 16 : 28];
            var count = read is null ? socket.GetRawSocketOption(family == AddressFamily.InterNetwork ? 0 : 41, 80, bytes)
                : read(socket, family == AddressFamily.InterNetwork ? 0 : 41, 80, bytes);
            if (count == bytes.Length) original = ParseOriginal(bytes, family);
        }
        catch (SocketException) { } // No metadata is never canonical authority.
        catch (ObjectDisposedException) { return null; }
        return new(local, peer, original);
    }
    public bool Matches(HttpContext context) => context.Connection.LocalPort == LocalPort &&
        context.Connection.LocalIpAddress is { } local && Normalize(local).ToString() == LocalAddress &&
        context.Connection.RemoteIpAddress is { } peer && Normalize(peer).ToString() == PeerAddress;
    public bool Canonical => LocalPort == 5445 && OriginalPort == 443 && OriginalAddress == LocalAddress &&
        PrivateOrLoopback(IPAddress.Parse(LocalAddress)) && PrivateOrLoopback(IPAddress.Parse(PeerAddress));
    public bool Physical => Canonical && IPAddress.IsLoopback(IPAddress.Parse(OriginalAddress!)) &&
        IPAddress.IsLoopback(IPAddress.Parse(LocalAddress)) && IPAddress.IsLoopback(IPAddress.Parse(PeerAddress));
    public bool LocalProbe => LocalPort == 5445 && IPAddress.IsLoopback(IPAddress.Parse(LocalAddress)) &&
        IPAddress.IsLoopback(IPAddress.Parse(PeerAddress)) && (OriginalPort is null || OriginalPort == 5445 && OriginalAddress == LocalAddress);
}
