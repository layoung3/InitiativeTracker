using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace InitiativeTracker.Web.Services;

public class TrackerUrlService
{
    private readonly IConfiguration
        _configuration;

    public TrackerUrlService(
        IConfiguration configuration)
    {
        _configuration =
            configuration;
    }


    public string GetBaseUrl(
        HttpRequest request)
    {
        var configured =
            _configuration[
                "Tracker:PublicBaseUrl"];

        if (!string.IsNullOrWhiteSpace(
                configured))
        {
            return configured
                .Trim()
                .TrimEnd('/');
        }


        /*
         * If the request already arrived through a LAN hostname
         * or LAN IP, keep using that exact address.
         */
        if (!IsLoopbackHost(
                request.Host.Host))
        {
            return
                $"{request.Scheme}://{request.Host}";
        }


        /*
         * Development commonly opens the DM UI as localhost.
         * A QR code containing localhost would point the player's
         * phone back at the phone itself, so replace it with the
         * hub's best LAN IPv4 address.
         */
        var lanAddress =
            FindPreferredLanAddress();

        if (lanAddress is null)
        {
            return
                $"{request.Scheme}://{request.Host}";
        }


        var scheme =
            _configuration[
                "Tracker:LanScheme"];

        if (string.IsNullOrWhiteSpace(
                scheme))
        {
            scheme =
                "http";
        }


        var configuredPort =
            _configuration
                .GetValue<int?>(
                    "Tracker:LanPort");

        var port =
            configuredPort ??
            request.Host.Port;


        var builder =
            new UriBuilder(
                scheme,
                lanAddress.ToString());

        if (port.HasValue &&
            !IsDefaultPort(
                scheme,
                port.Value))
        {
            builder.Port =
                port.Value;
        }

        return builder
            .Uri
            .GetLeftPart(
                UriPartial.Authority)
            .TrimEnd('/');
    }


    public string GetPlayerJoinUrl(
        HttpRequest request,
        string token)
    {
        var baseUrl =
            GetBaseUrl(request);

        return
            $"{baseUrl}/join?t=" +
            Uri.EscapeDataString(token);
    }


    public string GetManualJoinUrl(
        HttpRequest request)
    {
        return
            $"{GetBaseUrl(request)}/join";
    }


    private static bool IsLoopbackHost(
        string host)
    {
        if (string.IsNullOrWhiteSpace(
                host))
        {
            return true;
        }

        if (string.Equals(
                host,
                "localhost",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(
                host,
                out var address))
        {
            return IPAddress.IsLoopback(
                address);
        }

        return false;
    }


    private static IPAddress?
        FindPreferredLanAddress()
    {
        var candidates =
            new List<
                (IPAddress Address, int Score)>();

        foreach (var networkInterface
                 in NetworkInterface
                     .GetAllNetworkInterfaces())
        {
            if (networkInterface
                    .OperationalStatus !=
                OperationalStatus.Up)
            {
                continue;
            }

            if (networkInterface
                    .NetworkInterfaceType is
                NetworkInterfaceType.Loopback
                or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            IPInterfaceProperties properties;

            try
            {
                properties =
                    networkInterface
                        .GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }


            var hasGateway =
                properties
                    .GatewayAddresses
                    .Any(
                        gateway =>
                            gateway.Address
                                .AddressFamily ==
                            AddressFamily.InterNetwork &&
                            !gateway.Address.Equals(
                                IPAddress.Any));


            foreach (var unicast
                     in properties
                         .UnicastAddresses)
            {
                var address =
                    unicast.Address;

                if (address.AddressFamily !=
                        AddressFamily.InterNetwork ||
                    IPAddress.IsLoopback(
                        address) ||
                    IsLinkLocal(
                        address))
                {
                    continue;
                }


                var score =
                    0;

                if (hasGateway)
                {
                    score += 100;
                }

                if (networkInterface
                        .NetworkInterfaceType is
                    NetworkInterfaceType.Ethernet
                    or NetworkInterfaceType
                        .Wireless80211)
                {
                    score += 40;
                }

                if (IsPrivateIpv4(
                        address))
                {
                    score += 20;
                }


                candidates.Add(
                    (address, score));
            }
        }


        return candidates
            .OrderByDescending(
                candidate =>
                    candidate.Score)
            .Select(
                candidate =>
                    candidate.Address)
            .FirstOrDefault();
    }


    private static bool IsPrivateIpv4(
        IPAddress address)
    {
        var bytes =
            address.GetAddressBytes();

        return
            bytes[0] == 10 ||

            (bytes[0] == 172 &&
             bytes[1] >= 16 &&
             bytes[1] <= 31) ||

            (bytes[0] == 192 &&
             bytes[1] == 168);
    }


    private static bool IsLinkLocal(
        IPAddress address)
    {
        var bytes =
            address.GetAddressBytes();

        return
            bytes[0] == 169 &&
            bytes[1] == 254;
    }


    private static bool IsDefaultPort(
        string scheme,
        int port)
    {
        return
            string.Equals(
                scheme,
                "http",
                StringComparison.OrdinalIgnoreCase) &&
            port == 80 ||

            string.Equals(
                scheme,
                "https",
                StringComparison.OrdinalIgnoreCase) &&
            port == 443;
    }
}
