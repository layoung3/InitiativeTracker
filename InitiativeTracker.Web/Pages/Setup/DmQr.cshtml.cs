using System.Net;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;

namespace InitiativeTracker.Web.Pages.Setup;

public class DmQrModel : PageModel
{
    private readonly TrackerUrlService
        _trackerUrl;


    public DmQrModel(
        TrackerUrlService trackerUrl)
    {
        _trackerUrl =
            trackerUrl;
    }


    public IActionResult OnGet()
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


        var dmLoginUrl =
            _trackerUrl
                .GetBaseUrl(
                    Request)
                .TrimEnd('/') +
            "/dm/login";


        using var generator =
            new QRCodeGenerator();

        using var qrData =
            generator.CreateQrCode(
                dmLoginUrl,
                QRCodeGenerator.ECCLevel.Q);

        using var qrCode =
            new PngByteQRCode(
                qrData);

        var pngBytes =
            qrCode.GetGraphic(
                pixelsPerModule:
                    8);


        return File(
            pngBytes,
            "image/png");
    }
}
