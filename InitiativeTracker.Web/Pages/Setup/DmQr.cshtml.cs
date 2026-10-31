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
    private readonly DmAuthenticationPolicy _authentication;


    public DmQrModel(
        TrackerUrlService trackerUrl, DmAuthenticationPolicy authentication)
    {
        _trackerUrl =
            trackerUrl;
        _authentication = authentication;
    }


    public IActionResult OnGet()
    {
        if (!_authentication.PhysicalScreen(HttpContext))
        {
            return NotFound();
        }


        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var dmLoginUrl = _authentication.RequiresTrustedPin
            ? _authentication.BrowserOrigin is { } origin ? origin + "/dm/login" : null
            : _trackerUrl
                .GetBaseUrl(
                    Request)
                .TrimEnd('/') +
            "/dm/login";
        if (dmLoginUrl is null) return StatusCode(403);


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
