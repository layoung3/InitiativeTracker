using InitiativeTracker.Core.Models;
using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Hubs;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;

namespace InitiativeTracker.Web.Pages.Player;

[Authorize(Roles = "Player")]
public class IndexModel : PageModel
{
    private readonly PlayerRegistryService _playerRegistry;
    private readonly CharacterService _characterService;
    private readonly EncounterStateService _encounterState;
    private readonly IHubContext<EncounterHub> _encounterHub;

    public IndexModel(PlayerRegistryService playerRegistry, CharacterService characterService, EncounterStateService encounterState, IHubContext<EncounterHub> encounterHub)
    {
        _playerRegistry = playerRegistry;
        _characterService = characterService;
        _encounterState = encounterState;
        _encounterHub = encounterHub;
    }

    public PlayerConnection Player { get; private set; } = null!;

    public IReadOnlyList<PlayerCharacter> Characters { get; private set; } = [];

    public IReadOnlyList<Combatant> ControlledCombatants { get; private set; } = [];

    public IReadOnlyList<Combatant> TemporaryControlledCombatants { get; private set; } = [];

    public Guid PlayerProfileId { get; private set; }

    public Guid CurrentDeviceCredentialId { get; private set; }

    public IReadOnlyList<PlayerDeviceCredential> LinkedDevices { get; private set; } = [];

    public string? NewRecoveryKey { get; private set; }

    public Encounter Encounter => _encounterState.CurrentEncounter;

    public IActionResult OnGet()
    {
        var playerClaim =
            User.FindFirst(
                "player_id")?.Value;

        if (!Guid.TryParse(
                playerClaim,
                out var playerId))
        {
            return RedirectToPage(
                "/Join/Index");
        }

        var player =
            _playerRegistry.GetPlayer(
                playerId);

        if (player is null ||
            !player.IsActive)
        {
            return RedirectToPage(
                "/Join/Index");
        }

        Player =
            player;

        _playerRegistry.UpdateLastSeen(
            playerId);

        var profileClaim =
            User.FindFirst(
                "player_profile_id")?.Value;

        if (!Guid.TryParse(
                profileClaim,
                out var profileId))
        {
            return RedirectToPage(
                "/Join/Index");
        }

        PlayerProfileId =
            profileId;

        var credentialClaim =
            User.FindFirst(
                "player_device_id")?.Value;

        if (!Guid.TryParse(
                credentialClaim,
                out var credentialId))
        {
            return RedirectToPage(
                "/Join/Index");
        }

        CurrentDeviceCredentialId =
            credentialId;

        LinkedDevices =
            _playerRegistry
                .GetPlayerDevices(
                    profileId);

        NewRecoveryKey =
            TempData[
                "NewRecoveryKey"]
            as string;

        Characters =
            _characterService
                .GetCharacters(
                    profileId);

        var characterIds =
            Characters
                .Select(x => x.Id)
                .ToHashSet();

        ControlledCombatants =
            _encounterState
                .CurrentEncounter
                .Combatants
                .Where(
                    x =>
                        x.AssignedPlayerProfileId ==
                        profileId)
                .OrderBy(x => x.SortOrder)
                .ToList();

        TemporaryControlledCombatants =
            ControlledCombatants
                .Where(
                    combatant =>
                        !combatant.PlayerCharacterId.HasValue ||
                        !characterIds.Contains(
                            combatant.PlayerCharacterId.Value))
                .ToList();

        return Page();
    }

    private bool TryGetPlayerProfileId(
        out Guid playerProfileId)
    {
        var claim =
            User.FindFirst(
                "player_profile_id")?.Value;

        return Guid.TryParse(
            claim,
            out playerProfileId);
    }

    public async Task<IActionResult> OnPostAddCharacterAsync(Guid characterId)
    {
        if (!TryGetPlayerIdentity(out var playerId, out var profileId, out var credentialId))
            return Unauthorized();
        var player = _playerRegistry.GetPlayer(playerId);
        if (player is null || !player.IsActive || player.PlayerProfileId != profileId ||
            player.PlayerDeviceCredentialId != credentialId ||
            !_playerRegistry.IsActivePlayerCredential(credentialId, profileId))
            return Forbid();
        try
        {
            _encounterState.AddPlayerCharacter(characterId, submittingPlayerProfileId: profileId);
            await BroadcastEncounterAsync();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            TempData["AccountError"] = ex.Message;
        }
        return RedirectToPage();
    }

    public IActionResult OnPostCreateCharacter(
        string name)
    {
        var profileClaim =
            User.FindFirst(
                "player_profile_id")?.Value;

        if (!Guid.TryParse(
                profileClaim,
                out var profileId))
        {
            return Unauthorized();
        }

        _characterService.CreateCharacter(
            profileId,
            name);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetInitiativeAsync(
        Guid characterId,
        int initiative)
    {
        var profileClaim =
            User.FindFirst(
                "player_profile_id")?.Value;

        if (!Guid.TryParse(
                profileClaim,
                out var profileId))
        {
            return Unauthorized();
        }

        var character =
            _characterService
                .GetCharacter(
                    characterId);

        if (character is null ||
            character.PlayerProfileId != profileId)
        {
            return Forbid();
        }

        _encounterState
            .SetPlayerCharacterInitiative(
                characterId,
                initiative);

        await _encounterHub
            .Clients
            .All
            .SendAsync(
                "EncounterUpdated",
                _encounterState
                    .GetDisplayState());

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAdjustHpAsync(
        Guid combatantId,
        int amount)
    {
        if (!TryGetPlayerProfileId(
                out var profileId))
        {
            return Unauthorized();
        }

        try
        {
            _encounterState
                .AdjustHpForController(
                    profileId,
                    combatantId,
                    amount);

            await BroadcastEncounterAsync();
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or InvalidOperationException)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            return RedirectToPage();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetHpAsync(
        Guid combatantId,
        int? currentHp)
    {
        if (!TryGetPlayerProfileId(
                out var profileId))
        {
            return Unauthorized();
        }

        if (!currentHp.HasValue)
        {
            return RedirectToPage();
        }

        try
        {
            _encounterState
                .SetCurrentHpForController(
                    profileId,
                    combatantId,
                    currentHp.Value);

            await BroadcastEncounterAsync();
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or InvalidOperationException)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetTemporaryHpAsync(
        Guid combatantId,
        int temporaryHp)
    {
        if (!TryGetPlayerProfileId(
                out var profileId))
        {
            return Unauthorized();
        }

        try
        {
            _encounterState
                .SetTemporaryHpForController(
                    profileId,
                    combatantId,
                    temporaryHp);

            await BroadcastEncounterAsync();
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or InvalidOperationException)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEndTurnAsync()
    {
        if (!TryGetPlayerProfileId(
                out var profileId))
        {
            return Unauthorized();
        }

        try
        {
            _encounterState
                .AdvanceTurnForController(
                    profileId);

            await BroadcastEncounterAsync();
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or InvalidOperationException)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);
        }

        return RedirectToPage();
    }

    public IActionResult OnPostRegenerateRecoveryKey()
    {
        if (!TryGetPlayerProfileId(
                out var profileId))
        {
            return Unauthorized();
        }

        var recoveryKey =
            _playerRegistry
                .RegenerateRecoveryKey(
                    profileId);

        TempData[
            "NewRecoveryKey"] =
            recoveryKey;

        return RedirectToPage();
    }


    public IActionResult OnPostRenameDevice(
        Guid credentialId,
        string deviceName)
    {
        if (!TryGetPlayerProfileId(
                out var profileId))
        {
            return Unauthorized();
        }

        try
        {
            _playerRegistry
                .RenameDevice(
                    profileId,
                    credentialId,
                    deviceName);
        }
        catch (ArgumentException ex)
        {
            TempData[
                "AccountError"] =
                ex.Message;
        }

        return RedirectToPage();
    }


    public async Task<IActionResult>
        OnPostRevokeDeviceAsync(
            Guid credentialId)
    {
        if (!TryGetPlayerIdentity(
                out var playerId,
                out var profileId,
                out var currentCredentialId))
        {
            return Unauthorized();
        }

        IReadOnlyList<Guid>
            removedConnectionIds;

        try
        {
            removedConnectionIds =
                _playerRegistry
                    .RevokeDevice(
                        profileId,
                        credentialId);
        }
        catch (ArgumentException ex)
        {
            TempData[
                "AccountError"] =
                ex.Message;

            return RedirectToPage();
        }

        await NotifyRemovedPlayersAsync(
            removedConnectionIds);

        if (credentialId ==
            currentCredentialId)
        {
            Response.Cookies.Delete(
                "InitiativeTracker.PlayerDevice");

            await HttpContext
                .SignOutAsync(
                    CookieAuthenticationDefaults
                        .AuthenticationScheme);

            return RedirectToPage(
                "/Join/Index");
        }

        return RedirectToPage();
    }


    public async Task<IActionResult>
        OnPostSignOutAsync()
    {
        var playerClaim =
            User.FindFirst(
                "player_id")?.Value;

        if (Guid.TryParse(
                playerClaim,
                out var playerId))
        {
            _playerRegistry
                .RemovePlayer(
                    playerId);
        }

        /*
         * Sign out of the current game but keep the remembered
         * PlayerDevice cookie. The next visit to /join can offer
         * "Continue as <name>" without using the recovery key.
         */
        await HttpContext
            .SignOutAsync(
                CookieAuthenticationDefaults
                    .AuthenticationScheme);

        return RedirectToPage(
            "/Join/Index");
    }


    public async Task<IActionResult>
        OnPostForgetThisDeviceAsync()
    {
        if (!TryGetPlayerIdentity(
                out var playerId,
                out var profileId,
                out var credentialId))
        {
            return Unauthorized();
        }

        IReadOnlyList<Guid>
            removedConnectionIds;

        try
        {
            removedConnectionIds =
                _playerRegistry
                    .RevokeDevice(
                        profileId,
                        credentialId);
        }
        catch (ArgumentException)
        {
            removedConnectionIds =
                [playerId];
        }

        await NotifyRemovedPlayersAsync(
            removedConnectionIds);

        Response.Cookies.Delete(
            "InitiativeTracker.PlayerDevice");

        await HttpContext
            .SignOutAsync(
                CookieAuthenticationDefaults
                    .AuthenticationScheme);

        return RedirectToPage(
            "/Join/Index");
    }


    private bool TryGetPlayerIdentity(
        out Guid playerId,
        out Guid playerProfileId,
        out Guid credentialId)
    {
        playerId =
            Guid.Empty;

        playerProfileId =
            Guid.Empty;

        credentialId =
            Guid.Empty;

        return
            Guid.TryParse(
                User.FindFirst(
                    "player_id")?.Value,
                out playerId) &&

            Guid.TryParse(
                User.FindFirst(
                    "player_profile_id")?.Value,
                out playerProfileId) &&

            Guid.TryParse(
                User.FindFirst(
                    "player_device_id")?.Value,
                out credentialId);
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


    private async Task BroadcastEncounterAsync()
    {
        var displayState =
            _encounterState
                .GetDisplayState();

        await _encounterHub
            .Clients
            .All
            .SendAsync(
                "EncounterUpdated",
                displayState);
    }
}