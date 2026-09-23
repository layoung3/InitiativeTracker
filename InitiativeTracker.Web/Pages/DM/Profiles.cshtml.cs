using InitiativeTracker.Web.Models;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.Dm;

[Authorize(Roles = "DM")]
public class ProfilesModel : PageModel
{
    private readonly PlayerRegistryService
        _playerRegistry;

    private readonly CharacterService
        _characterService;

    private readonly EncounterStateService
        _encounterState;

    public ProfilesModel(
        PlayerRegistryService playerRegistry,
        CharacterService characterService,
        EncounterStateService encounterState)
    {
        _playerRegistry =
            playerRegistry;

        _characterService =
            characterService;

        _encounterState =
            encounterState;
    }

    public IReadOnlyList<PlayerProfileAdminViewModel>
        Profiles
    { get; private set; } = [];

    public void OnGet()
    {
        LoadProfiles();
    }

    public IActionResult OnPostDeleteProfile(
        Guid profileId)
    {
        var encounter =
            _encounterState.CurrentEncounter;

        var profile =
            _playerRegistry
                .GetAllProfiles()
                .SingleOrDefault(
                    x => x.Id == profileId);

        if (profile is null)
        {
            return RedirectToPage();
        }

        var characters =
            _characterService
                .GetCharacters(profileId);

        var characterIds =
            characters
                .Select(x => x.Id)
                .ToHashSet();

        var isReferenced =
            encounter.Combatants.Any(
                x =>
                    x.AssignedPlayerProfileId ==
                        profileId ||
                    (
                        x.PlayerCharacterId.HasValue &&
                        characterIds.Contains(
                            x.PlayerCharacterId.Value)
                    ));

        if (isReferenced)
        {
            ModelState.AddModelError(
                string.Empty,
                "This profile is currently referenced by the encounter and cannot be deleted.");

            LoadProfiles();

            return Page();
        }

        _playerRegistry
            .PermanentlyDeleteProfile(
                profileId);

        return RedirectToPage();
    }

    public IActionResult OnPostDeleteCharacter(
        Guid characterId)
    {
        var encounter =
            _encounterState.CurrentEncounter;

        var isReferenced =
            encounter.Combatants.Any(
                x =>
                    x.PlayerCharacterId ==
                    characterId);

        if (isReferenced)
        {
            ModelState.AddModelError(
                string.Empty,
                "This character is currently in the encounter and cannot be deleted.");

            LoadProfiles();

            return Page();
        }

        _characterService
            .PermanentlyDeleteCharacter(
                characterId);

        return RedirectToPage();
    }

    private void LoadProfiles()
    {
        var encounter =
            _encounterState.CurrentEncounter;

        Profiles =
            _playerRegistry
                .GetAllProfiles()
                .Select(profile =>
                {
                    var characters =
                        _characterService
                            .GetCharacters(
                                profile.Id);

                    var characterModels =
                        characters
                            .Select(character =>
                                new PlayerProfileCharacterAdminViewModel
                                {
                                    CharacterId =
                                        character.Id,

                                    CharacterName =
                                        character.Name,

                                    IsInEncounter =
                                        encounter.Combatants.Any(
                                            x =>
                                                x.PlayerCharacterId ==
                                                character.Id)
                                })
                            .ToList();

                    return new PlayerProfileAdminViewModel
                    {
                        ProfileId =
                            profile.Id,

                        PlayerName =
                            profile.Name,

                        UpdatedAtUtc =
                            profile.UpdatedAtUtc,

                        IsReferencedByEncounter =
                            encounter.Combatants.Any(
                                x =>
                                    x.AssignedPlayerProfileId ==
                                    profile.Id) ||
                            characterModels.Any(
                                x => x.IsInEncounter),

                        Characters =
                            characterModels
                    };
                })
                .ToList();
    }
}