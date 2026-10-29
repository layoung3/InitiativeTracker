using InitiativeTracker.Core.Networking;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.Dm;

[Authorize(Roles = "DM")]
public class NetworkModel(NetworkStatusService networkStatus, NetworkCommandBridge networkCommands) : PageModel
{
    public NetworkStatusService.Snapshot Status { get; private set; } = null!;
    [BindProperty] public string? SelectedSsid { get; set; }
    [BindProperty] public string? HiddenSsid { get; set; }
    [BindProperty] public bool UseHiddenSsid { get; set; }
    [BindProperty] public bool RequiresPassword { get; set; } = true;
    [BindProperty] public string? WifiPassword { get; set; }
    public string? ValidationSummary { get; private set; }
    public bool CommandsEnabled => networkCommands.Enabled;
    [BindProperty] public string? SavedUuid { get; set; }
    public string? CommandResult { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        SetSensitiveResponseHeaders();
        Status = await networkStatus.GetAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostValidateAsync(CancellationToken cancellationToken)
    {
        SetSensitiveResponseHeaders();
        Status = await networkStatus.GetAsync(cancellationToken);
        var ssid = UseHiddenSsid ? HiddenSsid : SelectedSsid;
        var errors = WifiProvisioningValidation.Validate(ssid, WifiPassword, RequiresPassword).ToList();
        if (!UseHiddenSsid && !Status.Networks.Any(x => x.Ssid == ssid))
            errors.Add("Select a network from the current scan results, or choose manual network entry.");

        // Password deliberately discarded before rendering; never save to TempData, session, DB, or log.
        WifiPassword = null;
        ModelState.Remove(nameof(WifiPassword));
        if (errors.Count > 0)
        {
            foreach (var error in errors) ModelState.AddModelError(string.Empty, error);
        }
        else
        {
            ValidationSummary = "Input format validated. No Wi-Fi credentials were saved and no connection was changed. " +
                "Actual provisioning will be enabled only after satellite coordination and watchdog recovery are tested.";
        }
        return Page();
    }

    public async Task<IActionResult> OnPostProvisionAsync(CancellationToken cancellationToken)
    {
        SetSensitiveResponseHeaders();
        // Clear secret from bound model before rendering any HTML response.
        var ssid = HiddenSsid;
        var password = WifiPassword;
        WifiPassword = null;
        ModelState.Remove(nameof(WifiPassword));
        if (string.IsNullOrWhiteSpace(ssid) || password is null ||
            WifiProvisioningValidation.Validate(ssid, password, true).Any())
        {
            ModelState.AddModelError(string.Empty, "Provide a valid SSID and WPA-Personal password.");
        }
        else
        {
            var result = await networkCommands.ProvisionAsync(ssid, password, cancellationToken);
            if (result.Success) CommandResult = result.Message;
            else ModelState.AddModelError(string.Empty, result.Message);
        }
        Status = await networkStatus.GetAsync(cancellationToken);
        return Page();
    }

    [BindProperty] public string? NewHotspotPassword { get; set; }
    [BindProperty] public string? ConfirmHotspotPassword { get; set; }

    public async Task<IActionResult> OnPostRotateHotspotPasswordAsync(CancellationToken cancellationToken)
    {
        SetSensitiveResponseHeaders();
        var password = NewHotspotPassword;
        var confirmation = ConfirmHotspotPassword;
        NewHotspotPassword = null;
        ConfirmHotspotPassword = null;
        ModelState.Remove(nameof(NewHotspotPassword));
        ModelState.Remove(nameof(ConfirmHotspotPassword));
        Status = await networkStatus.GetAsync(cancellationToken);
        if (Status.Connection == "InitiativeTracker-Recovery" || !Status.IsConnected)
            ModelState.AddModelError(string.Empty, "Connect the hub to external Wi-Fi before changing the private hotspot password.");
        else if (password != confirmation || password is null)
            ModelState.AddModelError(string.Empty, "Hotspot passwords do not match.");
        else
        {
            var result = await networkCommands.RotateHotspotPasswordAsync(password, cancellationToken);
            if (result.Success) CommandResult = result.Message;
            else ModelState.AddModelError(string.Empty, result.Message);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostBeginReturnAsync(CancellationToken cancellationToken)
    {
        SetSensitiveResponseHeaders();
        Status = await networkStatus.GetAsync(cancellationToken);
        if (Status.Connection != "InitiativeTracker-Recovery")
            ModelState.AddModelError(string.Empty, "Connect to the private hotspot before requesting a saved Wi-Fi return.");
        else if (!Status.SavedNetworks.Any(x => x.Uuid == SavedUuid))
            ModelState.AddModelError(string.Empty, "Choose an existing saved Wi-Fi profile.");
        else
        {
            var result = await networkCommands.RunAsync("begin-return", SavedUuid, cancellationToken);
            if (result.Success) CommandResult = result.Message;
            else ModelState.AddModelError(string.Empty, result.Message);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmReturnAsync(CancellationToken cancellationToken)
    {
        SetSensitiveResponseHeaders();
        Status = await networkStatus.GetAsync(cancellationToken);
        var result = await networkCommands.RunAsync("confirm-return", null, cancellationToken);
        if (result.Success) CommandResult = "Connection confirmation accepted. The rollback deadline is cleared.";
        else ModelState.AddModelError(string.Empty, result.Message);
        return Page();
    }

    public async Task<IActionResult> OnPostBeginHotspotAsync(CancellationToken cancellationToken)
    {
        SetSensitiveResponseHeaders();
        Status = await networkStatus.GetAsync(cancellationToken);
        if (!Status.IsConnected || Status.Connection is "InitiativeTracker-Recovery" or "Unavailable")
            ModelState.AddModelError(string.Empty, "A working external Wi-Fi connection is required to begin the supervised hotspot switch.");
        else
        {
            var result = await networkCommands.RunAsync("begin-hotspot", null, cancellationToken);
            if (result.Success) CommandResult = result.Message;
            else ModelState.AddModelError(string.Empty, result.Message);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmHotspotAsync(CancellationToken cancellationToken)
    {
        SetSensitiveResponseHeaders();
        Status = await networkStatus.GetAsync(cancellationToken);
        if (Status.Connection != "InitiativeTracker-Recovery")
            ModelState.AddModelError(string.Empty, "Connect through the private hotspot to confirm the switch.");
        else
        {
            var result = await networkCommands.RunAsync("confirm-hotspot", null, cancellationToken);
            if (result.Success) CommandResult = "Private hotspot confirmed. Automatic return to the previous Wi-Fi is cancelled.";
            else ModelState.AddModelError(string.Empty, result.Message);
        }
        return Page();
    }

    private void SetSensitiveResponseHeaders()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
    }
}
