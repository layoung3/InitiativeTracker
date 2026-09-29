using InitiativeTracker.Core.Models;
using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Data.Enums;
using InitiativeTracker.Web.Hubs;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;

namespace InitiativeTracker.Web.Pages.Player;

[Authorize(Roles = "Player")]
public class CharacterModel : PageModel
{
    private readonly CharacterService _characterService;
    private readonly EncounterStateService _encounterState;
    private readonly IHubContext<EncounterHub> _encounterHub;

    public CharacterModel(
    CharacterService characterService,
    EncounterStateService encounterState,
    IHubContext<EncounterHub> encounterHub)
    {
        _characterService = characterService;
        _encounterState = encounterState;
        _encounterHub = encounterHub;
    }

    public string? OpenEditor{ get; private set; }

    public PlayerCharacter Character { get; private set; } = null!;

    public IReadOnlyList<PlayerCharacterSkill> Skills{ get; private set; } = [];

    public Combatant? EncounterCombatant { get; private set; }

    public Guid PlayerProfileId { get; private set; }

    public bool IsControlled =>
        EncounterCombatant is not null &&
        EncounterCombatant
            .AssignedPlayerProfileId ==
        PlayerProfileId;

    public bool IsCurrent =>
        EncounterCombatant is not null &&
        _encounterState
            .CurrentEncounter
            .CurrentCombatantId ==
        EncounterCombatant.Id;

    public bool IsEncounterStarted =>
        _encounterState
            .CurrentEncounter
            .IsStarted;

    public int EncounterRound =>
        _encounterState
            .CurrentEncounter
            .Round;

    public int DisplayCurrentHp =>
        EncounterCombatant?.CurrentHp ??
        Character.CurrentHp;

    public int DisplayMaxHp =>
        EncounterCombatant?.MaxHp ??
        Character.MaxHp;

    public int DisplayTemporaryHp =>
        EncounterCombatant?.TemporaryHp ??
        Character.TemporaryHp;

    public bool CanChangeLiveHp =>
        EncounterCombatant is null ||
        IsControlled;


    public IActionResult OnGet(Guid id, string? editor=null)
    {
        OpenEditor = editor;

        if (!TryGetPlayerProfileId(
                out var profileId))
        {
            return Unauthorized();
        }


        var character = _characterService.GetCharacter(id);

        if (character is null)
        {
            return NotFound();
        }


        if (character.PlayerProfileId != profileId)
        {
            return Forbid();
        }

        PlayerProfileId = profileId;

        Character = character;

        Skills = _characterService.GetSkills(character.Id);

        EncounterCombatant =
            _encounterState
                .CurrentEncounter
                .Combatants
                .SingleOrDefault(
                    x =>
                        x.PlayerCharacterId ==
                        character.Id);

        return Page();
    }


    public int GetAbilityModifier(int score)
    {
        return (int)Math.Floor(
            (score - 10) / 2.0);
    }


    public string FormatModifier(int score)
    {
        var modifier =
            GetAbilityModifier(score);

        return modifier >= 0
            ? $"+{modifier}"
            : modifier.ToString();
    }

    public int GetSavingThrowBonus(
        int score,
        bool proficient)
    {
        var bonus =
            GetAbilityModifier(score);

        if (proficient)
        {
            bonus += Character.ProficiencyBonus;
        }

        return bonus;
    }

    public string FormatBonus(int bonus)
    {
        return bonus >= 0
            ? $"+{bonus}"
            : bonus.ToString();
    }

    public int GetSkillBonus(PlayerCharacterSkill skill)
    {
        if (skill.OverrideBonus.HasValue)
        {
            return skill.OverrideBonus.Value;
        }

        var abilityScore =
            GetSkillAbilityScore(
                skill.Skill);

        var bonus =
            GetAbilityModifier(
                abilityScore);

        bonus +=
            skill.ProficiencyLevel switch
            {
                SkillProficiencyLevel.Proficient =>
                    Character.ProficiencyBonus,

                SkillProficiencyLevel.Expertise =>
                    Character.ProficiencyBonus * 2,

                _ => 0
            };

        bonus +=
            skill.AdditionalBonus;

        return bonus;
    }

    public int GetSkillAbilityScore(CharacterSkillType skill)
    {
        return skill switch
        {
            CharacterSkillType.Athletics =>
                Character.Strength,

            CharacterSkillType.Acrobatics or
            CharacterSkillType.SleightOfHand or
            CharacterSkillType.Stealth =>
                Character.Dexterity,

            CharacterSkillType.Arcana or
            CharacterSkillType.History or
            CharacterSkillType.Investigation or
            CharacterSkillType.Nature or
            CharacterSkillType.Religion =>
                Character.Intelligence,

            CharacterSkillType.AnimalHandling or
            CharacterSkillType.Insight or
            CharacterSkillType.Medicine or
            CharacterSkillType.Perception or
            CharacterSkillType.Survival =>
                Character.Wisdom,

            CharacterSkillType.Deception or
            CharacterSkillType.Intimidation or
            CharacterSkillType.Performance or
            CharacterSkillType.Persuasion =>
                Character.Charisma,

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(skill),
                    skill,
                    null)
        };
    }

    public string GetSkillAbilityName(CharacterSkillType skill)
    {
        return skill switch
        {
            CharacterSkillType.Athletics =>
                "STR",

            CharacterSkillType.Acrobatics or
            CharacterSkillType.SleightOfHand or
            CharacterSkillType.Stealth =>
                "DEX",

            CharacterSkillType.Arcana or
            CharacterSkillType.History or
            CharacterSkillType.Investigation or
            CharacterSkillType.Nature or
            CharacterSkillType.Religion =>
                "INT",

            CharacterSkillType.AnimalHandling or
            CharacterSkillType.Insight or
            CharacterSkillType.Medicine or
            CharacterSkillType.Perception or
            CharacterSkillType.Survival =>
                "WIS",

            CharacterSkillType.Deception or
            CharacterSkillType.Intimidation or
            CharacterSkillType.Performance or
            CharacterSkillType.Persuasion =>
                "CHA",

            _ => "—"
        };
    }

    public string GetSkillName(CharacterSkillType skill)
    {
        return skill switch
        {
            CharacterSkillType.AnimalHandling =>
                "Animal Handling",

            CharacterSkillType.SleightOfHand =>
                "Sleight of Hand",

            _ =>
                skill.ToString()
        };
    }


    private bool TryGetPlayerProfileId(out Guid playerProfileId)
    {
        var claim =
            User.FindFirst(
                "player_profile_id")?.Value;

        return Guid.TryParse(
            claim,
            out playerProfileId);
    }

    public async Task<IActionResult> OnPostEndTurnAsync(Guid id)
    {
        if (!TryGetPlayerProfileId(
                out var profileId))
        {
            return Unauthorized();
        }


        var character =
            _characterService
                .GetCharacter(id);

        if (character is null)
        {
            return NotFound();
        }


        if (character.PlayerProfileId !=
            profileId)
        {
            return Forbid();
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


        return RedirectToPage(
            new
            {
                id
            });
    }

    private async Task BroadcastEncounterAsync()
    {
        await _encounterHub
            .Clients
            .All
            .SendAsync(
                "EncounterUpdated",
                _encounterState
                    .GetDisplayState());
    }

    public async Task<IActionResult>OnPostAdjustHpAsync(Guid id, int amount)
    {
        var ownershipResult =
            ValidateOwnedCharacter(
                id,
                out var character,
                out var profileId);

        if (ownershipResult is not null)
        {
            return ownershipResult;
        }

        var combatant =
            _encounterState
                .CurrentEncounter
                .Combatants
                .SingleOrDefault(
                    x =>
                        x.PlayerCharacterId ==
                        character!.Id);

        if (combatant is null)
        {
            _characterService
                .AdjustCharacterHp(
                    character.Id,
                    amount);
        }
        else
        {
            _encounterState
                .AdjustHpForController(
                    profileId,
                    combatant.Id,
                    amount);

            await BroadcastEncounterAsync();
        }

        return RedirectToPage(
            new
            {
                id, editor = "hp"
            });
    }

    public async Task<IActionResult>OnPostSetHpAsync(Guid id, int? currentHp)
    {
        if (!currentHp.HasValue)
        {
            return RedirectToPage(
                new
                {
                    id
                });
        }

        var ownershipResult =
            ValidateOwnedCharacter(
                id,
                out var character,
                out var profileId);

        if (ownershipResult is not null)
        {
            return ownershipResult;
        }

        var combatant =
            _encounterState
                .CurrentEncounter
                .Combatants
                .SingleOrDefault(
                    x =>
                        x.PlayerCharacterId ==
                        character!.Id);

        if (combatant is null)
        {
            _characterService
                .SetCharacterCurrentHp(
                    character.Id,
                    currentHp.Value);
        }
        else
        {
            _encounterState
                .SetCurrentHpForController(
                    profileId,
                    combatant.Id,
                    currentHp.Value);

            await BroadcastEncounterAsync();
        }

        return RedirectToPage(
            new
            {
                id, editor = "hp"
            });
    }

    public async Task<IActionResult>OnPostSetTemporaryHpAsync(Guid id, int temporaryHp)
    {
        var ownershipResult =
            ValidateOwnedCharacter(
                id,
                out var character,
                out var profileId);

        if (ownershipResult is not null)
        {
            return ownershipResult;
        }

        var combatant =
            _encounterState
                .CurrentEncounter
                .Combatants
                .SingleOrDefault(
                    x =>
                        x.PlayerCharacterId ==
                        character!.Id);

        if (combatant is null)
        {
            _characterService
                .SetCharacterTemporaryHp(
                    character.Id,
                    temporaryHp);
        }
        else
        {
            _encounterState
                .SetTemporaryHpForController(
                    profileId,
                    combatant.Id,
                    temporaryHp);

            await BroadcastEncounterAsync();
        }

        return RedirectToPage(
            new
            {
                id, editor = "temp"
            });
    }

    public IActionResult OnPostToggleInspiration(Guid id)
    {
        var ownershipResult =
            ValidateOwnedCharacter(
                id,
                out var character,
                out _);

        if (ownershipResult is not null)
        {
            return ownershipResult;
        }

        _characterService
            .SetCharacterInspiration(
                character!.Id,
                !character.Inspiration);

        return RedirectToPage(
            new
            {
                id
            });
    }

    public IActionResult OnPostSetExhaustion(Guid id, int exhaustionLevel)
    {
        var ownershipResult =
            ValidateOwnedCharacter(
                id,
                out var character,
                out _);

        if (ownershipResult is not null)
        {
            return ownershipResult;
        }

        _characterService
            .SetCharacterExhaustion(
                character!.Id,
                exhaustionLevel);

        return RedirectToPage(
            new
            {
                id
            });
    }

    private IActionResult? ValidateOwnedCharacter(
        Guid characterId,
        out PlayerCharacter? character,
        out Guid playerProfileId)
    {
        character =
            null;

        playerProfileId =
            Guid.Empty;

        if (!TryGetPlayerProfileId(
                out playerProfileId))
        {
            return Unauthorized();
        }

        character =
            _characterService
                .GetCharacter(
                    characterId);

        if (character is null)
        {
            return NotFound();
        }

        if (character.PlayerProfileId !=
            playerProfileId)
        {
            return Forbid();
        }

        return null;
    }
}