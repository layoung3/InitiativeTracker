using InitiativeTracker.Web.Models;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.SignalR;

namespace InitiativeTracker.Web.Hubs;

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
}