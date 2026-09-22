using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.Display;

[Authorize(Roles = "Display")]
public class SettingsModel : PageModel
{
    private readonly DeviceRegistryService
        _deviceRegistry;

    public SettingsModel(
        DeviceRegistryService deviceRegistry)
    {
        _deviceRegistry =
            deviceRegistry;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult>
        OnPostDisconnectAsync()
    {
        var deviceClaim =
            User.FindFirst(
                "device_id")?.Value;

        if (Guid.TryParse(
                deviceClaim,
                out var deviceId))
        {
            _deviceRegistry
                .ForgetDevice(
                    deviceId);
        }

        await HttpContext
            .SignOutAsync();

        return RedirectToPage(
            "/Display/Pair");
    }
}