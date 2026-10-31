using InitiativeTracker.Core.Networking;
using InitiativeTracker.Core.Coordination;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.Dm;

[Authorize(Policy = DmAuthenticationPolicy.NetworkPolicy)]
public class NetworkModel(NetworkStatusService networkStatus, NetworkCommandBridge networkCommands, DeviceCoordinationService coordination, IConfiguration configuration, IWebHostEnvironment environment, DeviceRegistryService displays, NetworkAdminAuthority authority) : PageModel
{
    public bool NetworkAuthority => authority.Allows(HttpContext);
    public string? PhysicalAuthorityCode => LocalCredentials ? authority.PhysicalCode() : null;
    [BindProperty] public string NetworkAuthorityCode { get; set; } = "";
    public IActionResult OnPostUnlockNetwork()
    {
        var cookie = authority.Unlock(HttpContext, NetworkAuthorityCode);
        if (cookie is null) return StatusCode(403);
        Response.Cookies.Append(NetworkAdminAuthority.CookieName,cookie,new CookieOptions {Secure=true,HttpOnly=true,SameSite=SameSiteMode.Strict,Path="/",MaxAge=TimeSpan.FromMinutes(15)});
        return Redirect("/dm/network");
    }
    public bool CoordinationEnabled => coordination.Enabled;
    public bool LocalCredentials => authority.PhysicalScreen(HttpContext);
    public bool CanSubmitSecrets => authority.TrustedHttps(HttpContext);
    public DeviceCoordinationService.Journal Coordination => coordination.Snapshot;
    [NonHandler]
    public bool Online(Guid id) => coordination.Online(id);
    public string? HubPin => LocalCredentials && CoordinationEnabled
        ? DeviceProtocol.Fingerprint(DeviceProtocol.Certificate(Path.Combine(CoordinationEndpoints.DataDirectory(configuration, environment), "coordination", "hub.pfx"), "InitiativeTracker Hub", true)) : null;
    [BindProperty] public Guid TransitionId { get; set; }
    [BindProperty] public string TargetKind { get; set; } = "wifi";
    [BindProperty] public Guid[] LeaveBehind { get; set; } = [];
    public IReadOnlyList<InitiativeTracker.Web.Data.RegisteredDevice> PairedDisplays => displays.GetTrustedDisplays();
    [BindProperty] public Guid? AgentDisplayId { get; set; }
    [BindProperty] public Guid AgentIntentId { get; set; }
    [BindProperty] public Guid AgentId { get; set; }
    [BindProperty] public string VerifiedPin { get; set; } = "";
    public Task<IActionResult> OnPostPrepareAsync(CancellationToken ct) => Coordinate(() => coordination.PrepareAsync(TargetKind, TargetKind == "wifi" ? SavedUuid : null, ct), ct);
    public Task<IActionResult> OnPostCommitAsync(CancellationToken ct) => Coordinate(() => { coordination.Commit(TransitionId, LeaveBehind, DateTimeOffset.UtcNow); return Task.CompletedTask; }, ct);
    public Task<IActionResult> OnPostPostponeAsync(CancellationToken ct) => Coordinate(() => { coordination.Postpone(TransitionId); return Task.CompletedTask; }, ct);
    public Task<IActionResult> OnPostConfirmCoordinatedAsync(CancellationToken ct) => Coordinate(() => coordination.ConfirmAsync(TransitionId, ct), ct);
    public Task<IActionResult> OnPostApproveAgentAsync(CancellationToken ct) => Coordinate(() =>
    {
        if (!LocalCredentials) throw new InvalidOperationException("Approve from the physical hub screen");
        coordination.Approve(AgentId, VerifiedPin, DateTimeOffset.UtcNow, AgentDisplayId, AgentIntentId); return Task.CompletedTask;
    }, ct);
    public Task<IActionResult> OnPostRevokeAgentAsync(CancellationToken ct) => Coordinate(() => { coordination.Revoke(AgentId); return Task.CompletedTask; }, ct);
    private async Task<IActionResult> Coordinate(Func<Task> operation, CancellationToken ct)
    {
        SetSensitiveResponseHeaders();
        try { await operation(); CommandResult = "Coordination operation saved."; }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Net.Sockets.SocketException or OperationCanceledException)
        { ModelState.AddModelError(string.Empty, ex is InvalidOperationException ? ex.Message : "Network bridge unavailable; inspect local status before retrying."); }
        Status = await networkStatus.GetAsync(ct); return Page();
    }
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
        if (!CanSubmitSecrets) return StatusCode(403);
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
        if (!CanSubmitSecrets) return StatusCode(403);
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
        if (!CanSubmitSecrets) return StatusCode(403);
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
        if (CoordinationEnabled) return BadRequest("Use coordinated switching and confirmation.");
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
        if (CoordinationEnabled) return BadRequest("Use coordinated switching and confirmation.");
        SetSensitiveResponseHeaders();
        Status = await networkStatus.GetAsync(cancellationToken);
        var result = await networkCommands.RunAsync("confirm-return", null, cancellationToken);
        if (result.Success) CommandResult = "Connection confirmation accepted. The rollback deadline is cleared.";
        else ModelState.AddModelError(string.Empty, result.Message);
        return Page();
    }

    public async Task<IActionResult> OnPostBeginHotspotAsync(CancellationToken cancellationToken)
    {
        if (CoordinationEnabled) return BadRequest("Use coordinated switching and confirmation.");
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
        if (CoordinationEnabled) return BadRequest("Use coordinated switching and confirmation.");
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
