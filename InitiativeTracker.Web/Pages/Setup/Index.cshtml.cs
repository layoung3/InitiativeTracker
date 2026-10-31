using System.Net;
using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;
using System.Text;

namespace InitiativeTracker.Web.Pages.Setup;

public class IndexModel : PageModel
{
    private readonly GameSessionService
        _gameSession;

    private readonly TrackerUrlService
        _trackerUrl;
    private readonly NetworkCommandBridge _networkBridge;
    private readonly NetworkStatusService _networkStatus;
    private readonly NetworkAdminAuthority _networkAuthority;
    public string? NetworkAuthorityCode { get; private set; }
    public NetworkStatusService.Snapshot? Network { get; private set; }


    public IndexModel(
        GameSessionService gameSession,
        TrackerUrlService trackerUrl,
        NetworkCommandBridge networkBridge, NetworkStatusService networkStatus, NetworkAdminAuthority networkAuthority)
    {
        _gameSession =
            gameSession;

        _trackerUrl =
            trackerUrl;
        _networkBridge = networkBridge;
        _networkStatus = networkStatus;
        _networkAuthority = networkAuthority;
    }


    public GameSessionRecord Session =>
        _gameSession.CurrentSession;


    public string DisplayPairingUrl { get; private set; } =
        string.Empty;

    public string DisplayQrDataUri { get; private set; } = string.Empty;

    public string DmLoginUrl { get; private set; } =
        string.Empty;

    public string NetworkPageUrl { get; private set; } = string.Empty;

    public string NetworkQrDataUri { get; private set; } = string.Empty;
    public string HotspotJoinQrDataUri { get; private set; } = string.Empty;
    public bool HotspotJoinQrAvailable => HotspotJoinQrDataUri.Length > 0;

    public string PlayerQrDataUri { get; private set; } =
        string.Empty;


    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var remoteIp =
            HttpContext.Connection
                .RemoteIpAddress;

        if (remoteIp is null ||
            !IPAddress.IsLoopback(
                remoteIp))
        {
            return NotFound();
        }


        if (CoordinationEndpoints.Local(HttpContext)) NetworkAuthorityCode = _networkAuthority.PhysicalCode();
        Network = await _networkStatus.GetAsync(cancellationToken);

        var baseUrl =
            _trackerUrl
                .GetBaseUrl(
                    Request)
                .TrimEnd('/');


        DmLoginUrl =
            $"{baseUrl}/dm/login";


        DisplayPairingUrl =
            $"{baseUrl}/display/connect?token=" +
            Uri.EscapeDataString(
                Session.DisplayToken);


        using var displayData = QRCodeGenerator.GenerateQrCode(DisplayPairingUrl, QRCodeGenerator.ECCLevel.Q);
        using var displayQr = new SvgQRCode(displayData);
        DisplayQrDataUri = "data:image/svg+xml;base64," +
            Convert.ToBase64String(Encoding.UTF8.GetBytes(displayQr.GetGraphic(8)));

        // Generate the player invitation QR inside the locally restricted
        // setup page. No anonymous QR endpoint is needed.
        var playerJoinUrl =
            _trackerUrl.GetPlayerJoinUrl(
                Request,
                Session.PlayerJoinToken);

        using var qrData =
            QRCodeGenerator.GenerateQrCode(
                playerJoinUrl,
                QRCodeGenerator.ECCLevel.Q);

        using var qrCode = new SvgQRCode(qrData);
        var svg = qrCode.GetGraphic(8);

        PlayerQrDataUri =
            "data:image/svg+xml;base64," +
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(svg));

        NetworkPageUrl = $"{baseUrl}/dm/network";
        using var networkQrData = QRCodeGenerator.GenerateQrCode(
            NetworkPageUrl, QRCodeGenerator.ECCLevel.Q);
        using var networkQr = new SvgQRCode(networkQrData);
        NetworkQrDataUri = "data:image/svg+xml;base64," +
            Convert.ToBase64String(Encoding.UTF8.GetBytes(networkQr.GetGraphic(8)));

        // Only the physical kiosk (/setup over loopback) may request the
        // hotspot secret. Never embed it on remote-accessible DM/player pages.
        var wifiPayload = await _networkBridge.GetHotspotJoinQrAsync(cancellationToken);
        if (wifiPayload is not null)
        {
            using var wifiData = QRCodeGenerator.GenerateQrCode(wifiPayload, QRCodeGenerator.ECCLevel.Q);
            using var wifiQr = new SvgQRCode(wifiData);
            HotspotJoinQrDataUri = "data:image/svg+xml;base64," +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(wifiQr.GetGraphic(8)));
        }
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return Page();
    }
}
