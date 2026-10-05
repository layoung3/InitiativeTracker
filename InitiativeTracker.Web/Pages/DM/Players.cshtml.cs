using InitiativeTracker.Web.Hubs;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;

namespace InitiativeTracker.Web.Pages.Dm;

[Authorize(Roles = "DM")]
public class PlayersModel : PageModel
{
    private readonly PlayerRegistryService
        _playerRegistry;

    private readonly EncounterStateService
        _encounterState;

    private readonly IHubContext<EncounterHub>
        _encounterHub;

    public PlayersModel(
        PlayerRegistryService playerRegistry,
        EncounterStateService encounterState,
        IHubContext<EncounterHub> encounterHub)
    {
        _playerRegistry =
            playerRegistry;

        _encounterState =
            encounterState;

        _encounterHub =
            encounterHub;
    }

    public IReadOnlyList<PlayerProfileAdminView>
        Profiles
    { get; private set; } =
        [];

    public string? RecoveryKeyToShow { get; private set; }

    public string? RecoveryPlayerName { get; private set; }


    public void OnGet()
    {
        LoadProfiles();

        RecoveryKeyToShow =
            TempData[
                "RecoveryKey"]
            as string;

        RecoveryPlayerName =
            TempData[
                "RecoveryPlayerName"]
            as string;
    }


    public IActionResult OnPostResetRecoveryKey(
        Guid profileId,
        string playerName)
    {
        try
        {
            var recoveryKey =
                _playerRegistry
                    .RegenerateRecoveryKey(
                        profileId);

            TempData[
                "RecoveryKey"] =
                recoveryKey;

            TempData[
                "RecoveryPlayerName"] =
                playerName;
        }
        catch (ArgumentException ex)
        {
            TempData[
                "PlayerAdminError"] =
                ex.Message;
        }

        return RedirectToPage();
    }


    public async Task<IActionResult>
        OnPostRevokeAllDevicesAsync(
            Guid profileId)
    {
        var removedConnectionIds =
            _playerRegistry
                .RevokeAllDevices(
                    profileId);

        await NotifyRemovedPlayersAsync(
            removedConnectionIds);

        return RedirectToPage();
    }


    public async Task<IActionResult>
        OnPostDeleteProfileAsync(
            Guid profileId)
    {
        var controlsCombatant =
            _encounterState
                .CurrentEncounter
                .Combatants
                .Any(
                    combatant =>
                        combatant.AssignedPlayerProfileId ==
                        profileId);

        if (controlsCombatant)
        {
            TempData[
                "PlayerAdminError"] =
                "Remove this player's encounter controller assignments before deleting the stored profile.";

            return RedirectToPage();
        }

        var removedConnectionIds =
            _playerRegistry
                .RevokeAllDevices(
                    profileId);

        await NotifyRemovedPlayersAsync(
            removedConnectionIds);

        _playerRegistry
            .PermanentlyDeleteProfile(
                profileId);

        return RedirectToPage();
    }


    private void LoadProfiles()
    {
        Profiles =
            _playerRegistry
                .GetProfileAdminViews();
    }


    private async Task NotifyRemovedPlayersAsync(
        IReadOnlyList<Guid>
            playerConnectionIds)
    {
        foreach (var playerConnectionId
                 in playerConnectionIds)
        {
            await _encounterHub
                .Clients
                .Group(
                    EncounterHub
                        .GetPlayerGroup(
                            playerConnectionId))
                .SendAsync(
                    "PlayerRemoved");
        }
    }
}
