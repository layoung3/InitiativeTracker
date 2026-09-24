using InitiativeTracker.Core.Models;
using InitiativeTracker.Web.Models;
using InitiativeTracker.Core.Enums;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using InitiativeTracker.Web.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace InitiativeTracker.Web.Pages.Dm;

[Authorize(Roles = "DM")]
public class IndexModel : PageModel
{
    private readonly EncounterStateService _encounterState;
    private readonly IHubContext<EncounterHub> _encounterHub;
    private readonly PlayerRegistryService _playerRegistry;
    private readonly CharacterService _characterService;
    private readonly GameSessionService _gameSession;

    public IndexModel(EncounterStateService encounterState, IHubContext<EncounterHub> encounterHub, 
        PlayerRegistryService playerRegistry, CharacterService characterService, GameSessionService gameSession)
    {
        _encounterState = encounterState;
        _encounterHub = encounterHub;
        _playerRegistry = playerRegistry;
        _characterService = characterService;
        _gameSession = gameSession;
    }

    public IReadOnlyList<ConnectedPlayerCharacter> ConnectedPlayerCharacters { get; private set; } = [];

    public IReadOnlyList<ConnectedPlayerOption> ConnectedPlayers { get; private set; } = [];

    public Encounter Encounter => _encounterState.CurrentEncounter;

    [BindProperty]
    public string CombatantName { get; set; } = string.Empty;

    [BindProperty]
    public int Initiative { get; set; }

    [BindProperty]
    public int? CurrentHp { get; set; }

    [BindProperty]
    public int? MaxHp { get; set; }

    [BindProperty]
    public int Quantity { get; set; } = 1;

    public bool CanUndo => _encounterState.CanUndo;

    public void OnGet()
    {
        LoadConnectedPlayers();
        LoadConnectedPlayerCharacters();
    }

    public async Task<IActionResult> OnPostAddPlayerCharacterAsync(
        Guid characterId)
    {
        try
        {
            _encounterState
                .AddPlayerCharacter(
                    characterId);

            await BroadcastEncounterAsync();

            return RedirectToPage();
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or InvalidOperationException)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            LoadConnectedPlayerCharacters();

            return Page();
        }
    }

    public async Task<IActionResult> OnPostAddCombatant()
    {
        if (string.IsNullOrWhiteSpace(
            CombatantName))
        {
            ModelState.AddModelError(
                nameof(CombatantName),
                "Name is required.");

            return Page();
        }

        if (Quantity < 1 ||
            Quantity > 50)
        {
            ModelState.AddModelError(
                nameof(Quantity),
                "Quantity must be between 1 and 50.");

            return Page();
        }

        try
        {
            _encounterState.AddCombatants(
                CombatantName,
                Initiative,
                CurrentHp,
                MaxHp,
                Quantity);

            await BroadcastEncounterAsync();
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or InvalidOperationException)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateCombatant(
        Guid id,
        string name,
        int initiative,
        int? currentHp,
        int? maxHp)
    {
        try
        {
            _encounterState.UpdateCombatant(
                id,
                name,
                initiative,
                currentHp,
                maxHp);

            await BroadcastEncounterAsync();
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or InvalidOperationException)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteCombatant(
        Guid id)
    {
        try
        {
            _encounterState.DeleteCombatant(id);

            await BroadcastEncounterAsync();
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or InvalidOperationException)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostStartCombat()
    {
        try
        {
            _encounterState.StartCombat();
            await BroadcastEncounterAsync();
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostNextTurn()
    {
        try
        {
            _encounterState.AdvanceTurn();
            await BroadcastEncounterAsync();
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReset()
    {
        _encounterState.ResetEncounter();

        await BroadcastEncounterAsync();

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAdjustHp( Guid id, int amount)
    {
        try
        {
            _encounterState.AdjustHp(
                id,
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

            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetHp(
    Guid id,
    int? currentHp)
    {
        if (!currentHp.HasValue)
        {
            ModelState.AddModelError(
                string.Empty,
                "Current HP is required.");

            LoadConnectedPlayers();
            LoadConnectedPlayerCharacters();

            return Page();
        }

        try
        {
            _encounterState.SetCurrentHp(
                id,
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

            LoadConnectedPlayers();
            LoadConnectedPlayerCharacters();

            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetTemporaryHpAsync(
    Guid id,
    int temporaryHp)
    {
        try
        {
            _encounterState.SetTemporaryHp(
                id,
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

            LoadConnectedPlayers();
            LoadConnectedPlayerCharacters();

            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDefeat( Guid id)
    {
        _encounterState.SetCombatantStatus(
            id,
            CombatantStatus.Defeated);

        await BroadcastEncounterAsync();

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemove( Guid id)
    {
        _encounterState.SetCombatantStatus(
            id,
            CombatantStatus.Removed);

        await BroadcastEncounterAsync();

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRestore( Guid id)
    {
        _encounterState.SetCombatantStatus(
            id,
            CombatantStatus.Active);

        await BroadcastEncounterAsync();

        return RedirectToPage();
    }

    private async Task BroadcastEncounterAsync()
    {
        var displayState =
            _encounterState.GetDisplayState();

        await _encounterHub.Clients.All.SendAsync(
            "EncounterUpdated",
            displayState);
    }

    public async Task<IActionResult> OnPostPreviousTurn()
    {
        try
        {
            _encounterState.PreviousTurn();

            await BroadcastEncounterAsync();
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUndo()
    {
        if (_encounterState.Undo())
        {
            await BroadcastEncounterAsync();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReorder(
        List<Guid> orderedIds)
    {
        try
        {
            _encounterState.ReorderCombatants(
                orderedIds);

            await BroadcastEncounterAsync();

            return new JsonResult(new
            {
                success = true
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    public async Task<IActionResult> OnPostMoveCombatant(
        Guid id,
        int direction)
    {
        try
        {
            _encounterState.MoveCombatant(
                id,
                direction);

            await BroadcastEncounterAsync();
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or ArgumentOutOfRangeException)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAssignControllerAsync(
        Guid combatantId,
        Guid? playerProfileId)
    {
        try
        {
            if (playerProfileId.HasValue)
            {
                var sessionId =
                    _gameSession
                        .CurrentSession
                        .SessionId;

                var isConnected =
                    _playerRegistry
                        .GetActivePlayers(
                            sessionId)
                        .Any(
                            x =>
                                x.PlayerProfileId ==
                                playerProfileId.Value);

                if (!isConnected)
                {
                    throw new InvalidOperationException(
                        "That player is no longer connected.");
                }
            }

            _encounterState
                .AssignCombatantController(
                    combatantId,
                    playerProfileId);

            await BroadcastEncounterAsync();

            return RedirectToPage();
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or InvalidOperationException)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            LoadConnectedPlayers();

            LoadConnectedPlayerCharacters();

            return Page();
        }
    }

    private void LoadConnectedPlayerCharacters()
    {
        var sessionId =
            _gameSession
                .CurrentSession
                .SessionId;

        var activeConnections =
            _playerRegistry
                .GetActivePlayers(
                    sessionId);

        var connectedPlayers =
            activeConnections
                .GroupBy(
                    x => x.PlayerProfileId)
                .Select(
                    group => group
                        .OrderByDescending(
                            x => x.JoinedAtUtc)
                        .First())
                .ToList();

        var profileIds =
            connectedPlayers
                .Select(
                    x => x.PlayerProfileId)
                .ToList();

        var characters =
            _characterService
                .GetCharactersForProfiles(
                    profileIds);

        ConnectedPlayerCharacters =
            characters
                .Select(character =>
                {
                    var connection =
                        connectedPlayers
                            .Single(
                                x =>
                                    x.PlayerProfileId ==
                                    character.PlayerProfileId);

                    var alreadyAdded =
                        Encounter.Combatants.Any(
                            x =>
                                x.PlayerCharacterId ==
                                character.Id);

                    return new ConnectedPlayerCharacter
                    {
                        PlayerProfileId =
                            connection.PlayerProfileId,

                        PlayerName =
                            connection.Name,

                        CharacterId =
                            character.Id,

                        CharacterName =
                            character.Name,

                        Level =
                            character.Level,

                        CurrentHp =
                            character.CurrentHp,

                        MaxHp =
                            character.MaxHp,

                        AlreadyAdded =
                            alreadyAdded
                    };
                })
                .OrderBy(x => x.PlayerName)
                .ThenBy(x => x.CharacterName)
                .ToList();
    }

    private void LoadConnectedPlayers()
    {
        var sessionId =
            _gameSession
                .CurrentSession
                .SessionId;

        var activeConnections =
            _playerRegistry
                .GetActivePlayers(
                    sessionId);

        ConnectedPlayers =
            activeConnections
                .GroupBy(
                    x => x.PlayerProfileId)
                .Select(
                    group =>
                    {
                        var latestConnection =
                            group
                                .OrderByDescending(
                                    x => x.JoinedAtUtc)
                                .First();

                        return new ConnectedPlayerOption
                        {
                            PlayerProfileId =
                                latestConnection
                                    .PlayerProfileId,

                            PlayerName =
                                latestConnection.Name
                        };
                    })
                .OrderBy(
                    x => x.PlayerName)
                .ToList();
    }
}