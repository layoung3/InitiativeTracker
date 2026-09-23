using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Hubs;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;

namespace InitiativeTracker.Web.Pages.Dm;

[Authorize(Roles = "DM")]
public class DevicesModel : PageModel
{
    private readonly DeviceRegistryService _deviceRegistry;
    private readonly IHubContext<EncounterHub> _encounterHub;
    private readonly PlayerRegistryService _playerRegistry;
    private readonly GameSessionService _gameSession;
    private readonly EncounterStateService _encounterState;

    public DevicesModel(
    DeviceRegistryService deviceRegistry,
    PlayerRegistryService playerRegistry,
    GameSessionService gameSession,
    IHubContext<EncounterHub> encounterHub,
    EncounterStateService encounterState)
    {
        _deviceRegistry = deviceRegistry;
        _playerRegistry = playerRegistry;
        _gameSession = gameSession;
        _encounterHub = encounterHub;
        _encounterState = encounterState;
    }

    public IReadOnlyList<DevicePairingRequest> PendingRequests
    { get; private set; } = [];

    public IReadOnlyList<RegisteredDevice> Displays
    { get; private set; } = [];

    public IReadOnlyList<PlayerConnection> Players
    { get; private set; } = [];

    public void OnGet()
    {
        LoadDevices();
    }

    public IActionResult OnPostApprove(
        Guid id)
    {
        _deviceRegistry
            .ApprovePairing(id);

        return RedirectToPage();
    }

    public IActionResult OnPostReject(
        Guid id)
    {
        _deviceRegistry
            .RejectPairing(id);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostForgetAsync(Guid id)
    {
        _deviceRegistry
            .ForgetDevice(id);

        await _encounterHub
            .Clients
            .Group(
                EncounterHub.GetDeviceGroup(
                    id))
            .SendAsync(
                "DeviceRevoked");

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemovePlayerAsync(Guid id)
    {
        var player =
            _playerRegistry.GetPlayer(id);

        _playerRegistry.RemovePlayer(id);

        if (player is not null)
        {
            _encounterState
                .ClearControllerAssignments(
                    player.PlayerProfileId);

            await _encounterHub
                .Clients
                .All
                .SendAsync(
                    "EncounterUpdated",
                    _encounterState
                        .GetDisplayState());
        }

        await _encounterHub
            .Clients
            .Group(
                EncounterHub.GetPlayerGroup(id))
            .SendAsync(
                "PlayerRemoved");

        return RedirectToPage();
    }

    public IActionResult OnPostRename(
        Guid id,
        string name)
    {
        _deviceRegistry
            .RenameDevice(
                id,
                name);

        return RedirectToPage();
    }

    private void LoadDevices()
    {
        PendingRequests =
            _deviceRegistry
                .GetPendingRequests();

        Displays =
            _deviceRegistry
                .GetTrustedDisplays();

        Players =
            _playerRegistry.GetActivePlayers(
                _gameSession
                    .CurrentSession
                    .SessionId);
    }
}