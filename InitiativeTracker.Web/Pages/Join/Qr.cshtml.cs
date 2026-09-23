using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;

namespace InitiativeTracker.Web.Pages.Join;

[Authorize(Roles = "DM,Display")]
public class QrModel : PageModel
{
    private readonly GameSessionService
        _gameSession;

    private readonly TrackerUrlService
        _trackerUrls;

    public QrModel(
        GameSessionService gameSession,
        TrackerUrlService trackerUrls)
    {
        _gameSession =
            gameSession;

        _trackerUrls =
            trackerUrls;
    }

    public IActionResult OnGet()
    {
        var joinUrl =
            _trackerUrls.GetPlayerJoinUrl(
                Request,
                _gameSession
                    .CurrentSession
                    .PlayerJoinToken);

        using var qrCodeData =
            QRCodeGenerator.GenerateQrCode(
                joinUrl,
                QRCodeGenerator.ECCLevel.Q);

        using var qrCode =
            new SvgQRCode(
                qrCodeData);

        var svg =
            qrCode.GetGraphic(8);

        return Content(
            svg,
            "image/svg+xml");
    }
}