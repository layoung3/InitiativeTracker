using InitiativeTracker.Web.Models;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace InitiativeTracker.Web.Hubs;

[Authorize(Roles = "DM,Display,Player")]
public class EncounterHub : Hub
{
    private readonly EncounterStateService _encounterState;

    private readonly DeviceRegistryService
        _deviceRegistry;

    public EncounterHub(
        EncounterStateService encounterState,
        DeviceRegistryService deviceRegistry)
    {
        _encounterState =
            encounterState;

        _deviceRegistry =
            deviceRegistry;
    }

    public EncounterDisplayState GetEncounterState()
    {
        return _encounterState.GetDisplayState();
    }

    public string GetDisplayTheme()
    {
        return _encounterState.CurrentDisplayTheme;
    }

    public override async Task OnConnectedAsync()
    {
        var deviceClaim =
            Context.User?
                .FindFirst(
                    "device_id")?
                .Value;

        if (Guid.TryParse(
                deviceClaim,
                out var deviceId))
        {
            _deviceRegistry
                .MarkDeviceSeen(
                    deviceId);

            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                GetDeviceGroup(
                    deviceId));
        }

        var playerClaim =
            Context.User?
                .FindFirst("player_id")?
                .Value;

        if (Guid.TryParse(
                playerClaim,
                out var playerId))
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                GetPlayerGroup(playerId));
        }

        await base.OnConnectedAsync();
    }

    public static string GetDeviceGroup(
        Guid deviceId)
    {
        return $"device:{deviceId}";
    }

    public static string GetPlayerGroup(
        Guid playerId)
    {
        return $"player:{playerId}";
    }
}