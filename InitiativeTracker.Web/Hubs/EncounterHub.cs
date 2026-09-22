using InitiativeTracker.Web.Models;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace InitiativeTracker.Web.Hubs;

[Authorize(Roles = "DM,Display,Player")]
public class EncounterHub : Hub
{
    private readonly EncounterStateService _encounterState;

    public EncounterHub(
        EncounterStateService encounterState)
    {
        _encounterState = encounterState;
    }

    public EncounterDisplayState GetEncounterState()
    {
        return _encounterState.GetDisplayState();
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
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                GetDeviceGroup(
                    deviceId));
        }

        await base.OnConnectedAsync();
    }

    public static string GetDeviceGroup(
        Guid deviceId)
    {
        return $"device:{deviceId}";
    }
}