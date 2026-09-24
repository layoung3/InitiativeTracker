using InitiativeTracker.Core.Models;
using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Hubs;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
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
            character.PlayerProfileId !=
                profileId)
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
}