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
using InitiativeTracker.Web.Rules.Dnd5e;
using System.Text.Json;

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

    public string? OpenEditor { get; private set; }

    public PlayerCharacter Character { get; private set; } = null!;

    public IReadOnlyList<PlayerCharacterSkill> Skills { get; private set; } = [];

    public IReadOnlyList<PlayerCharacterItem> InventoryItems { get; private set; } = [];

    public IReadOnlyDictionary<Guid, PlayerCharacterWeapon> WeaponsByItemId { get; private set; } = new Dictionary<Guid, PlayerCharacterWeapon>();

    public IReadOnlyList<Dnd5eWeaponDefinition> StandardWeapons => Dnd5eWeaponCatalog.StandardWeapons;

    public IReadOnlyList<Dnd5eAttackView> Attacks { get; private set; } = [];

    public IReadOnlyDictionary<Guid, PlayerCharacterAttack> CharacterAttacksById { get; private set; } = new Dictionary<Guid, PlayerCharacterAttack>();

    public PlayerCharacterSpellcasting? Spellcasting { get; private set; }

    public Dnd5eSpellcastingSummaryView? SpellcastingSummary { get; private set; }

    public IReadOnlyList<PlayerCharacterSpell> Spells { get; private set; } = [];

    public IReadOnlyDictionary<int, PlayerCharacterSpellSlot> SpellSlotsByLevel { get; private set; } = new Dictionary<int, PlayerCharacterSpellSlot>();

    public IReadOnlyDictionary<Guid, PlayerCharacterSpell> SpellsById { get; private set; } = new Dictionary<Guid, PlayerCharacterSpell>();

    public PlayerCharacterSpell? ActiveConcentrationSpell { get; private set; }

    public string? OpenTab { get; private set; }

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

    public IReadOnlyList<CharacterConditionView> ActiveConditions { get; private set; } = [];


    public IReadOnlyList<string> StandardConditions => Dnd5eConditionCatalog.StandardConditions;


    public bool CanChangeConditions =>
        EncounterCombatant is null ||
        IsControlled;


    public string? OpenModal { get; private set; }


    public IActionResult OnGet(Guid id, string? editor = null, string? modal = null, string? tab = null)
    {
        OpenEditor = editor;
        OpenModal = modal;
        OpenTab = tab;

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

        InventoryItems = _characterService.GetInventory(character.Id);

        var weapons = _characterService.GetWeapons(character.Id);

        WeaponsByItemId = weapons.ToDictionary(x => x.PlayerCharacterItemId);

        var characterAttacks =
            _characterService
                .GetCharacterAttacks(
                    character.Id);

        CharacterAttacksById =
            characterAttacks
                .ToDictionary(
                    x => x.Id);

        var calculatedAttacks = new List<Dnd5eAttackView>();

        calculatedAttacks.AddRange(
            weapons
                .Where(
                    x => x.Item.IsEquipped)
                .Select(
                    x =>
                        Dnd5eWeaponAttackCalculator
                            .Calculate(
                                character,
                                x)));

        calculatedAttacks.AddRange(
            characterAttacks
                .Where(
                    x => x.IsEnabled)
                .Select(
                    x =>
                        Dnd5eCharacterAttackCalculator
                            .Calculate(
                                character,
                                x)));

        Attacks =
            calculatedAttacks
                .OrderBy(
                    x => x.Name)
                .ToList();

        Spellcasting =
            _characterService
                .GetSpellcasting(
                    character.Id);


        Spells =
            _characterService
                .GetSpells(
                    character.Id);

        SpellsById =
            Spells
                .ToDictionary(
                    x => x.Id);


        if (Spellcasting?.ActiveConcentrationSpellId is Guid concentrationSpellId &&
            SpellsById.TryGetValue(
                concentrationSpellId,
                out var activeConcentrationSpell))
        {
            ActiveConcentrationSpell =
                activeConcentrationSpell;
        }


        if (Spellcasting is not null)
        {
            SpellcastingSummary =
                Dnd5eSpellcastingCalculator
                    .Calculate(
                        character,
                        Spellcasting);


            SpellSlotsByLevel =
                Spellcasting
                    .SpellSlots
                    .ToDictionary(
                        x => x.Level);
        }

        EncounterCombatant =
            _encounterState
                .CurrentEncounter
                .Combatants
                .SingleOrDefault(
                    x =>
                        x.PlayerCharacterId ==
                        character.Id);

        if (EncounterCombatant is not null)
        {
            ActiveConditions =
                EncounterCombatant
                    .Conditions
                    .OrderBy(
                        x => x.Name)
                    .Select(
                        x =>
                            new CharacterConditionView
                            {
                                Id =
                                    x.Id,

                                Name =
                                    x.Name,

                                IsCustom =
                                    x.IsCustom,

                                Notes =
                                    x.Notes
                            })
                    .ToList();
        }
        else
        {
            ActiveConditions =
                _characterService
                    .GetConditions(
                        character.Id)
                    .OrderBy(
                        x => x.Name)
                    .Select(
                        x =>
                            new CharacterConditionView
                            {
                                Id =
                                    x.Id,

                                Name =
                                    x.Name,

                                IsCustom =
                                    x.IsCustom,

                                Notes =
                                    x.Notes
                            })
                    .ToList();
        }

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

    public async Task<IActionResult> OnPostAdjustHpAsync(Guid id, int amount)
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
                id,
                editor = "hp"
            });
    }

    public async Task<IActionResult> OnPostSetHpAsync(Guid id, int? currentHp)
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
                id,
                editor = "hp"
            });
    }

    public async Task<IActionResult> OnPostSetTemporaryHpAsync(Guid id, int temporaryHp)
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
                id,
                editor = "temp"
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

    public async Task<IActionResult> OnPostSetExhaustionAsync(Guid id, int exhaustionLevel)
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

        _encounterState
            .SetCharacterExhaustion(
                character!.Id,
                exhaustionLevel);

        await BroadcastEncounterAsync();

        return RedirectToPage(
            new
            {
                id
            });
    }

    public IActionResult OnPostAddInventoryItem(
        Guid id,
        string itemName,
        CharacterItemType itemType,
        int quantity,
        bool isEquipped,
        string? itemNotes)
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


        try
        {
            _characterService
                .AddInventoryItem(
                    character!.Id,
                    itemName,
                    itemType,
                    quantity,
                    isEquipped,
                    itemNotes);
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or ArgumentOutOfRangeException)
        {
            TempData["InventoryError"] =
                ex.Message;
        }


        return RedirectToPage(
            new
            {
                id
            });
    }

    public IActionResult OnPostToggleInventoryItemEquipped(
        Guid id,
        Guid itemId)
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


        try
        {
            _characterService
                .ToggleInventoryItemEquipped(
                    character!.Id,
                    itemId);
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or InvalidOperationException)
        {
            TempData["InventoryError"] =
                ex.Message;
        }


        return RedirectToPage(
            new
            {
                id
            });
    }

    public IActionResult OnPostAdjustInventoryItemQuantity(
        Guid id,
        Guid itemId,
        int amount)
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


        try
        {
            _characterService
                .AdjustInventoryItemQuantity(
                    character!.Id,
                    itemId,
                    amount);
        }
        catch (ArgumentException ex)
        {
            TempData["InventoryError"] =
                ex.Message;
        }


        return RedirectToPage(
            new
            {
                id
            });
    }

    public IActionResult OnPostUpdateInventoryItem(
        Guid id,
        Guid itemId,
        string itemName,
        CharacterItemType itemType,
        int quantity,
        bool isEquipped,
        string? itemNotes)
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


        try
        {
            _characterService
                .UpdateInventoryItem(
                    character!.Id,
                    itemId,
                    itemName,
                    itemType,
                    quantity,
                    isEquipped,
                    itemNotes);
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or InvalidOperationException)
        {
            TempData["InventoryError"] =
                ex.Message;
        }


        return RedirectToPage(
            new
            {
                id
            });
    }

    public IActionResult OnPostDeleteInventoryItem(
        Guid id,
        Guid itemId)
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


        try
        {
            _characterService
                .DeleteInventoryItem(
                    character!.Id,
                    itemId);
        }
        catch (ArgumentException ex)
        {
            TempData["InventoryError"] =
                ex.Message;
        }


        return RedirectToPage(
            new
            {
                id
            });
    }

    public bool CanEquipInventoryItem(
        CharacterItemType itemType)
    {
        return itemType is
            CharacterItemType.Weapon or
            CharacterItemType.Armor or
            CharacterItemType.Equipment;
    }


    public int GetInventoryTypeSortOrder(
        CharacterItemType itemType)
    {
        return itemType switch
        {
            CharacterItemType.Weapon => 0,
            CharacterItemType.Armor => 1,
            CharacterItemType.Equipment => 2,
            CharacterItemType.Consumable => 3,
            CharacterItemType.General => 4,
            _ => 99
        };
    }


    public string GetInventoryTypeLabel(
        CharacterItemType itemType)
    {
        return itemType switch
        {
            CharacterItemType.Weapon =>
                "Weapons",

            CharacterItemType.Armor =>
                "Armor",

            CharacterItemType.Equipment =>
                "Equipment",

            CharacterItemType.Consumable =>
                "Consumables",

            CharacterItemType.General =>
                "General",

            _ =>
                itemType.ToString()
        };
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

    public CharacterConditionView? GetActiveCondition(string name)
    {
        return ActiveConditions
            .FirstOrDefault(
                x =>
                    string.Equals(
                        x.Name,
                        name,
                        StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IActionResult> OnPostToggleConditionAsync(
        Guid id,
        string conditionName)
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


        if (!Dnd5eConditionCatalog
                .StandardConditions
                .Any(
                    x =>
                        string.Equals(
                            x,
                            conditionName,
                            StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(
                "Unknown standard condition.");
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
            var existing =
                _characterService
                    .GetConditions(
                        character.Id)
                    .FirstOrDefault(
                        x =>
                            string.Equals(
                                x.Name,
                                conditionName,
                                StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                _characterService
                    .AddCondition(
                        character.Id,
                        conditionName,
                        isCustom: false);
            }
            else
            {
                _characterService
                    .RemoveCondition(
                        character.Id,
                        existing.Id);
            }
        }
        else
        {
            var existing =
                combatant.Conditions
                    .FirstOrDefault(
                        x =>
                            string.Equals(
                                x.Name,
                                conditionName,
                                StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                _encounterState
                    .AddConditionForController(
                        profileId,
                        combatant.Id,
                        conditionName,
                        isCustom: false);
            }
            else
            {
                _encounterState
                    .RemoveConditionForController(
                        profileId,
                        combatant.Id,
                        existing.Id);
            }

            await BroadcastEncounterAsync();
        }


        return RedirectToPage(
            new
            {
                id,
                modal =
                    "conditions"
            });
    }

    public async Task<IActionResult> OnPostAddCustomConditionAsync(
        Guid id,
        string customConditionName,
        string? customConditionNotes)
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


        if (string.IsNullOrWhiteSpace(
                customConditionName))
        {
            return RedirectToPage(
                new
                {
                    id,
                    modal =
                        "conditions"
                });
        }


        var combatant =
            _encounterState
                .CurrentEncounter
                .Combatants
                .SingleOrDefault(
                    x =>
                        x.PlayerCharacterId ==
                        character!.Id);


        try
        {
            if (combatant is null)
            {
                _characterService
                    .AddCondition(
                        character.Id,
                        customConditionName,
                        isCustom: true,
                        customConditionNotes);
            }
            else
            {
                _encounterState
                    .AddConditionForController(
                        profileId,
                        combatant.Id,
                        customConditionName,
                        isCustom: true,
                        customConditionNotes);

                await BroadcastEncounterAsync();
            }
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or InvalidOperationException)
        {
            /*
             * We can improve the actual modal validation
             * presentation later.
             */
            TempData["ConditionError"] =
                ex.Message;
        }


        return RedirectToPage(
            new
            {
                id,
                modal =
                    "conditions"
            });
    }

    public async Task<IActionResult> OnPostRemoveConditionAsync(
        Guid id,
        Guid conditionId)
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
                .RemoveCondition(
                    character.Id,
                    conditionId);
        }
        else
        {
            _encounterState
                .RemoveConditionForController(
                    profileId,
                    combatant.Id,
                    conditionId);

            await BroadcastEncounterAsync();
        }


        return RedirectToPage(
            new
            {
                id,
                modal =
                    "conditions"
            });
    }

    public string GetCharacterAttackConfigurationJson(
        Guid attackId)
    {
        if (!CharacterAttacksById.TryGetValue(
                attackId,
                out var attack))
        {
            return string.Empty;
        }


        return JsonSerializer.Serialize(
            new
            {
                id =
                    attack.Id,

                name =
                    attack.Name,

                attackAbility =
                    attack.AttackAbility.ToString(),

                addsAbilityModifierToHit =
                    attack.AddsAbilityModifierToHit,

                usesProficiency =
                    attack.UsesProficiency,

                attackBonus =
                    attack.AttackBonus,

                normalRange =
                    attack.NormalRange,

                longRange =
                    attack.LongRange,

                notes =
                    attack.Notes,

                damageComponents =
                    attack.DamageComponents
                        .OrderBy(
                            x => x.SortOrder)
                        .Select(
                            x =>
                                new
                                {
                                    damageExpression =
                                        x.DamageExpression,

                                    damageType =
                                        x.DamageType,

                                    addsAbilityModifier =
                                        x.AddsAbilityModifier,

                                    additionalDamageBonus =
                                        x.AdditionalDamageBonus
                                })
                        .ToList()
            });
    }

    public string GetWeaponConfigurationJson(
        Guid itemId)
    {
        if (!WeaponsByItemId.TryGetValue(
                itemId,
                out var weapon))
        {
            return string.Empty;
        }


        return JsonSerializer.Serialize(
            new
            {
                attackAbility =
                    weapon.AttackAbility.ToString(),

                usesProficiency =
                    weapon.UsesProficiency,

                attackBonus =
                    weapon.AttackBonus,

                primaryDamageBonus =
                    weapon.PrimaryDamageBonus,

                normalRange =
                    weapon.NormalRange,

                longRange =
                    weapon.LongRange,

                damageComponents =
                    weapon.DamageComponents
                        .OrderBy(
                            x => x.SortOrder)
                        .Select(
                            x =>
                                new
                                {
                                    damageDice =
                                        x.DamageDice,

                                    damageType =
                                        x.DamageType,

                                    addsAbilityModifier =
                                        x.AddsAbilityModifier,

                                    versatileDamageDice =
                                        x.VersatileDamageDice
                                })
                        .ToList()
            });
    }


    public string GetWeaponCatalogJson()
    {
        return JsonSerializer.Serialize(
            StandardWeapons
                .Select(
                    weapon =>
                        new
                        {
                            key =
                                weapon.Key,

                            name =
                                weapon.Name,

                            defaultAttackAbility =
                                weapon
                                    .DefaultAttackAbility
                                    .ToString(),

                            allowedAttackAbilities =
                                weapon
                                    .AllowedAttackAbilities
                                    .Select(
                                        x => x.ToString())
                                    .ToList(),

                            normalRange =
                                weapon.NormalRange,

                            longRange =
                                weapon.LongRange,

                            damageComponents =
                                weapon
                                    .DamageComponents
                                    .OrderBy(
                                        x => x.SortOrder)
                                    .Select(
                                        x =>
                                            new
                                            {
                                                damageDice =
                                                    x.DamageDice,

                                                damageType =
                                                    x.DamageType,

                                                addsAbilityModifier =
                                                    x.AddsAbilityModifier,

                                                versatileDamageDice =
                                                    x.VersatileDamageDice
                                            })
                                    .ToList()
                        })
                .ToList());
    }

    public IActionResult OnPostSaveWeaponConfiguration(
        Guid id,
        Guid itemId,
        CharacterAbilityType attackAbility,
        bool usesProficiency,
        int attackBonus,
        int primaryDamageBonus,
        int? normalRange,
        int? longRange,
        List<WeaponDamageInput> damageComponents)
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


        try
        {
            damageComponents ??=
                [];


            var weapon =
                new PlayerCharacterWeapon
                {
                    PlayerCharacterItemId =
                        itemId,

                    AttackAbility =
                        attackAbility,

                    UsesProficiency =
                        usesProficiency,

                    AttackBonus =
                        attackBonus,

                    PrimaryDamageBonus =
                        primaryDamageBonus,

                    NormalRange =
                        normalRange,

                    LongRange =
                        longRange,

                    DamageComponents =
                        damageComponents
                            .Select(
                                (damage, index) =>
                                    new PlayerCharacterWeaponDamageComponent
                                    {
                                        PlayerCharacterItemId =
                                            itemId,

                                        DamageDice =
                                            damage.DamageDice,

                                        DamageType =
                                            damage.DamageType,

                                        IsPrimary =
                                            index == 0,

                                        AddsAbilityModifier =
                                            damage.AddsAbilityModifier,

                                        VersatileDamageDice =
                                            index == 0
                                                ? damage.VersatileDamageDice
                                                : null,

                                        SortOrder =
                                            index
                                    })
                            .ToList()
                };


            _characterService
                .SaveWeapon(
                    character!.Id,
                    itemId,
                    weapon);
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or ArgumentOutOfRangeException
                  or InvalidOperationException)
        {
            TempData["InventoryError"] =
                ex.Message;
        }


        return RedirectToPage(
            new
            {
                id
            });
    }

    public IActionResult OnPostSaveCharacterAttack(
        Guid id,
        Guid? attackId,
        string attackName,
        CharacterAbilityType attackAbility,
        bool addsAbilityModifierToHit,
        bool usesProficiency,
        int attackBonus,
        int? normalRange,
        int? longRange,
        string? attackNotes,
        List<CharacterAttackDamageInput>? damageComponents)
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


        try
        {
            damageComponents ??=
                [];


            var attack =
                new PlayerCharacterAttack
                {
                    Name =
                        attackName,

                    AttackAbility =
                        attackAbility,

                    AddsAbilityModifierToHit =
                        addsAbilityModifierToHit,

                    UsesProficiency =
                        usesProficiency,

                    AttackBonus =
                        attackBonus,

                    NormalRange =
                        normalRange,

                    LongRange =
                        longRange,

                    Notes =
                        attackNotes,

                    DamageComponents =
                        damageComponents
                            .Select(
                                (damage, index) =>
                                    new PlayerCharacterAttackDamageComponent
                                    {
                                        DamageExpression =
                                            damage.DamageExpression,

                                        DamageType =
                                            damage.DamageType,

                                        AddsAbilityModifier =
                                            damage.AddsAbilityModifier,

                                        AdditionalDamageBonus =
                                            damage.AdditionalDamageBonus,

                                        SortOrder =
                                            index
                                    })
                            .ToList()
                };


            _characterService
                .SaveCharacterAttack(
                    character!.Id,
                    attackId,
                    attack);
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or ArgumentOutOfRangeException
                  or InvalidOperationException)
        {
            TempData["AttackError"] =
                ex.Message;
        }


        return RedirectToPage(
            new
            {
                id
            });
    }

    public IActionResult OnPostDeleteCharacterAttack(
        Guid id,
        Guid attackId)
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
            .DeleteCharacterAttack(
                character!.Id,
                attackId);


        return RedirectToPage(
            new
            {
                id
            });
    }

    public string FormatDamage(
        string dice,
        int bonus)
    {
        if (bonus > 0)
        {
            return $"{dice} + {bonus}";
        }


        if (bonus < 0)
        {
            return $"{dice} - {Math.Abs(bonus)}";
        }

        return dice;
    }

    public PlayerCharacterSpellSlot GetSpellSlot(
        int level)
    {
        return SpellSlotsByLevel
            .GetValueOrDefault(
                level);
    }

    public IReadOnlyList<int> GetVisibleSpellLevels()
    {
        var levels = new SortedSet<int>();

        if (Spellcasting is not null)
        {
            levels.Add(0);
        }

        foreach (var spell
                 in Spells)
        {
            levels.Add(
                spell.Level);
        }

        foreach (var level
                 in SpellSlotsByLevel.Keys)
        {
            levels.Add(
                level);
        }

        return levels
            .Where(
                x =>
                    x >= 0 &&
                    x <= 9)
            .ToList();
    }

    public string GetSpellLevelLabel(int level)
    {
        return level switch
        {
            0 => "Cantrips",
            1 => "1st Level",
            2 => "2nd Level",
            3 => "3rd Level",
            _ => $"{level}th Level"
        };
    }

    public IActionResult OnPostSaveSpellcasting(
        Guid id,
        CharacterAbilityType spellcastingAbility,
        int additionalSpellAttackBonus,
        int additionalSpellSaveDcBonus,
        List<SpellSlotMaximumInput>? spellSlots)
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


        try
        {
            spellSlots ??=
                [];


            var maximumSlots =
                spellSlots
                    .Where(
                        x =>
                            x.Level >= 1 &&
                            x.Level <= 9)
                    .GroupBy(
                        x => x.Level)
                    .ToDictionary(
                        x => x.Key,
                        x => x.Last().MaximumSlots);


            _characterService
                .SaveSpellcasting(
                    character!.Id,
                    spellcastingAbility,
                    additionalSpellAttackBonus,
                    additionalSpellSaveDcBonus,
                    maximumSlots);
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or ArgumentOutOfRangeException
                  or InvalidOperationException)
        {
            TempData["SpellError"] =
                ex.Message;
        }


        return RedirectToPage(
            new
            {
                id,
                tab = "spells"
            });
    }

    public IActionResult OnPostAdjustSpellSlot(
        Guid id,
        int level,
        int amount)
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


        try
        {
            _characterService
                .AdjustSpellSlot(
                    character!.Id,
                    level,
                    amount);
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or ArgumentOutOfRangeException
                  or InvalidOperationException)
        {
            TempData["SpellError"] =
                ex.Message;
        }


        return RedirectToPage(
            new
            {
                id,
                tab = "spells"
            });
    }

    public IActionResult OnPostRestoreAllSpellSlots(Guid id)
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


        try
        {
            _characterService
                .RestoreAllSpellSlots(
                    character!.Id);
        }
        catch (InvalidOperationException ex)
        {
            TempData["SpellError"] =
                ex.Message;
        }


        return RedirectToPage(
            new
            {
                id,
                tab = "spells"
            });
    }

    public string GetSpellRangeArea(
    PlayerCharacterSpell spell)
    {
        var parts =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(
                spell.Range))
        {
            parts.Add(
                spell.Range);
        }

        if (!string.IsNullOrWhiteSpace(
                spell.Area))
        {
            parts.Add(
                spell.Area);
        }

        return parts.Count == 0
            ? "—"
            : string.Join(
                " · ",
                parts);
    }


    public string GetSpellResolution(
        PlayerCharacterSpell spell)
    {
        var parts =
            new List<string>();

        if (spell.HasSpellAttackRoll)
        {
            parts.Add(
                "Spell Attack");
        }

        if (spell.SavingThrowAbility.HasValue)
        {
            parts.Add(
                $"{GetAbilityAbbreviation(
                    spell.SavingThrowAbility.Value)} Save");
        }

        return parts.Count == 0
            ? "—"
            : string.Join(
                " · ",
                parts);
    }


    public string GetSpellComponents(
        PlayerCharacterSpell spell)
    {
        var parts =
            new List<string>();

        if (spell.HasVerbalComponent)
        {
            parts.Add("V");
        }

        if (spell.HasSomaticComponent)
        {
            parts.Add("S");
        }

        if (spell.HasMaterialComponent)
        {
            parts.Add("M");
        }

        return parts.Count == 0
            ? "—"
            : string.Join(
                " ",
                parts);
    }


    public string GetSpellDurationSummary(
        PlayerCharacterSpell spell)
    {
        if (spell.RequiresConcentration)
        {
            return string.IsNullOrWhiteSpace(
                    spell.Duration)
                ? "C"
                : $"C · {spell.Duration}";
        }

        return spell.Duration ?? string.Empty;
    }

    private int GetAbilityScore(
    CharacterAbilityType ability)
    {
        return ability switch
        {
            CharacterAbilityType.Strength =>
                Character.Strength,

            CharacterAbilityType.Dexterity =>
                Character.Dexterity,

            CharacterAbilityType.Constitution =>
                Character.Constitution,

            CharacterAbilityType.Intelligence =>
                Character.Intelligence,

            CharacterAbilityType.Wisdom =>
                Character.Wisdom,

            CharacterAbilityType.Charisma =>
                Character.Charisma,

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(ability),
                    ability,
                    null)
        };
    }

    public int GetCantripScalingSteps()
    {
        return Character.Level switch
        {
            >= 17 => 3,
            >= 11 => 2,
            >= 5 => 1,
            _ => 0
        };
    }

    public int GetSpellEffectFlatBonus(
        PlayerCharacterSpellEffectComponent effect)
    {
        var flatBonus =
            effect.AdditionalBonus;

        if (effect.AddsSpellcastingAbilityModifier &&
            Spellcasting is not null)
        {
            flatBonus +=
                GetAbilityModifier(
                    GetAbilityScore(
                        Spellcasting.SpellcastingAbility));
        }

        return flatBonus;
    }

    public string GetSpellEffectLabel(
        PlayerCharacterSpellEffectComponent effect)
    {
        return effect.EffectType switch
        {
            SpellEffectType.Healing =>
                "Healing",

            SpellEffectType.Damage
                when !string.IsNullOrWhiteSpace(
                    effect.DamageType) =>
                effect.DamageType,

            _ =>
                "Damage"
        };
    }

    public string FormatSpellEffect(
        PlayerCharacterSpellEffectComponent effect)
    {
        var flatBonus =
            GetSpellEffectFlatBonus(
                effect);

        var expression =
            effect.Expression;

        if (flatBonus > 0)
        {
            expression +=
                $" + {flatBonus}";
        }
        else if (flatBonus < 0)
        {
            expression +=
                $" - {Math.Abs(flatBonus)}";
        }

        if (SpellsById.TryGetValue(
                effect.PlayerCharacterSpellId,
                out var spell) &&
            spell.Level == 0 &&
            !string.IsNullOrWhiteSpace(
                effect.CantripScalingExpression))
        {
            var scalingSteps =
                GetCantripScalingSteps();

            if (scalingSteps == 1)
            {
                expression +=
                    $" + {effect.CantripScalingExpression}";
            }
            else if (scalingSteps > 1)
            {
                expression +=
                    $" + {scalingSteps} × {effect.CantripScalingExpression}";
            }
        }

        var result =
            $"{expression} {GetSpellEffectLabel(effect)}";

        if (SpellsById.TryGetValue(
                effect.PlayerCharacterSpellId,
                out var parentSpell) &&
            parentSpell.Level > 0 &&
            !string.IsNullOrWhiteSpace(
                effect.UpcastExpressionPerLevel))
        {
            result +=
                $" (+{effect.UpcastExpressionPerLevel}/lv)";
        }

        return result;
    }

    public string GetSpellConfigurationJson(
        Guid spellId)
    {
        if (!SpellsById.TryGetValue(
                spellId,
                out var spell))
        {
            return string.Empty;
        }

        var castSlots =
            SpellSlotsByLevel
                .Values
                .Where(
                    x =>
                        spell.Level > 0 &&
                        x.Level >= spell.Level)
                .OrderBy(
                    x => x.Level)
                .Select(
                    x =>
                        new
                        {
                            level =
                                x.Level,

                            remainingSlots =
                                x.RemainingSlots,

                            maximumSlots =
                                x.MaximumSlots
                        })
                .ToList();

        return JsonSerializer.Serialize(
            new
            {
                id = spell.Id,
                name = spell.Name,
                level = spell.Level,
                levelLabel = GetSpellLevelLabel(spell.Level),
                school = spell.School,
                castingTime = spell.CastingTime,
                range = spell.Range,
                area = spell.Area,
                rangeArea = GetSpellRangeArea(spell),
                duration = spell.Duration,
                hasVerbalComponent = spell.HasVerbalComponent,
                hasSomaticComponent = spell.HasSomaticComponent,
                hasMaterialComponent = spell.HasMaterialComponent,
                components = GetSpellComponents(spell),
                materialDescription = spell.MaterialDescription,
                requiresConcentration = spell.RequiresConcentration,
                isRitual = spell.IsRitual,
                isPrepared = spell.IsPrepared,
                hasSpellAttackRoll = spell.HasSpellAttackRoll,
                savingThrowAbility = spell.SavingThrowAbility?.ToString(),
                resolution = GetSpellResolution(spell),
                notes = spell.Notes,
                cantripScalingSteps = spell.Level == 0 ? GetCantripScalingSteps() : 0,
                activeConcentrationSpellId = Spellcasting?.ActiveConcentrationSpellId,
                activeConcentrationSpellName = ActiveConcentrationSpell?.Name,
                castSlots,
                effects =
                    spell.EffectComponents
                        .OrderBy(x => x.SortOrder)
                        .Select(
                            x => new
                            {
                                effectType = x.EffectType.ToString(),
                                expression = x.Expression,
                                damageType = x.DamageType,
                                effectLabel = GetSpellEffectLabel(x),
                                addsSpellcastingAbilityModifier = x.AddsSpellcastingAbilityModifier,
                                additionalBonus = x.AdditionalBonus,
                                flatBonus = GetSpellEffectFlatBonus(x),
                                upcastExpressionPerLevel = x.UpcastExpressionPerLevel,
                                cantripScalingExpression = x.CantripScalingExpression,
                                display = FormatSpellEffect(x)
                            })
                        .ToList()
            });
    }


    public string GetAbilityAbbreviation(
        CharacterAbilityType ability)
    {
        return ability switch
        {
            CharacterAbilityType.Strength =>
                "STR",

            CharacterAbilityType.Dexterity =>
                "DEX",

            CharacterAbilityType.Constitution =>
                "CON",

            CharacterAbilityType.Intelligence =>
                "INT",

            CharacterAbilityType.Wisdom =>
                "WIS",

            CharacterAbilityType.Charisma =>
                "CHA",

            _ =>
                "—"
        };
    }

    public IActionResult OnPostSaveSpell(
    Guid id,
    Guid? spellId,
    string spellName,
    int spellLevel,
    string? school,
    string? castingTime,
    string? range,
    string? area,
    string? duration,
    bool hasVerbalComponent,
    bool hasSomaticComponent,
    bool hasMaterialComponent,
    string? materialDescription,
    bool requiresConcentration,
    bool isRitual,
    bool isPrepared,
    bool hasSpellAttackRoll,
    CharacterAbilityType? savingThrowAbility,
    string? spellNotes,
    List<SpellEffectInput>? effects)
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

        try
        {
            effects ??=
                [];

            var spell =
                new PlayerCharacterSpell
                {
                    Name =
                        spellName,

                    Level =
                        spellLevel,

                    School =
                        school,

                    CastingTime =
                        castingTime,

                    Range =
                        range,

                    Area =
                        area,

                    Duration =
                        duration,

                    HasVerbalComponent =
                        hasVerbalComponent,

                    HasSomaticComponent =
                        hasSomaticComponent,

                    HasMaterialComponent =
                        hasMaterialComponent,

                    MaterialDescription =
                        materialDescription,

                    RequiresConcentration =
                        requiresConcentration,

                    IsRitual =
                        isRitual,

                    IsPrepared =
                        isPrepared,

                    HasSpellAttackRoll =
                        hasSpellAttackRoll,

                    SavingThrowAbility =
                        savingThrowAbility,

                    Notes =
                        spellNotes,

                    EffectComponents =
                        effects
                            .Select(
                                (effect, index) =>
                                    new PlayerCharacterSpellEffectComponent
                                    {
                                        EffectType =
                                            effect.EffectType,

                                        Expression =
                                            effect.Expression,

                                        DamageType =
                                            effect.DamageType,

                                        AddsSpellcastingAbilityModifier =
                                            effect.AddsSpellcastingAbilityModifier,

                                        AdditionalBonus =
                                            effect.AdditionalBonus,

                                        UpcastExpressionPerLevel =
                                            effect.UpcastExpressionPerLevel,

                                        CantripScalingExpression =
                                            effect.CantripScalingExpression,

                                        SortOrder =
                                            index
                                    })
                            .ToList()
                };

            _characterService
                .SaveSpell(
                    character!.Id,
                    spellId,
                    spell);
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or ArgumentOutOfRangeException
                  or InvalidOperationException)
        {
            TempData["SpellError"] =
                ex.Message;
        }

        return RedirectToPage(
            new
            {
                id,
                tab = "spells"
            });
    }

    public IActionResult OnPostCastSpell(
        Guid id,
        Guid spellId,
        int? slotLevel,
        bool castAsRitual)
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

        try
        {
            _characterService
                .CastSpell(
                    character!.Id,
                    spellId,
                    slotLevel,
                    castAsRitual);
        }
        catch (Exception ex)
            when (ex is ArgumentException
                  or ArgumentOutOfRangeException
                  or InvalidOperationException)
        {
            TempData["SpellError"] =
                ex.Message;
        }

        return RedirectToPage(
            new
            {
                id,
                tab = "spells"
            });
    }

    public IActionResult OnPostEndConcentration(
        Guid id)
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
            .EndConcentration(
                character!.Id);

        return RedirectToPage(
            new
            {
                id,
                tab = "spells"
            });
    }

    public IActionResult OnPostDeleteSpell(
    Guid id,
    Guid spellId)
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
            .DeleteSpell(
                character!.Id,
                spellId);

        return RedirectToPage(
            new
            {
                id,
                tab = "spells"
            });
    }

    public IActionResult OnPostToggleSpellPrepared(
        Guid id,
        Guid spellId)
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
            .ToggleSpellPrepared(
                character!.Id,
                spellId);

        return RedirectToPage(
            new
            {
                id,
                tab = "spells"
            });
    }

    public class CharacterConditionView
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public bool IsCustom { get; set; }

        public string? Notes { get; set; }
    }

    public class WeaponDamageInput
    {
        public string DamageDice
        {
            get;
            set;
        } = string.Empty;

        public string DamageType
        {
            get;
            set;
        } = string.Empty;

        public bool AddsAbilityModifier
        {
            get;
            set;
        }

        public string? VersatileDamageDice
        {
            get;
            set;
        }
    }

    public class CharacterAttackDamageInput
    {
        public string DamageExpression
        {
            get;
            set;
        } = string.Empty;

        public string DamageType
        {
            get;
            set;
        } = string.Empty;

        public bool AddsAbilityModifier
        {
            get;
            set;
        }

        public int AdditionalDamageBonus
        {
            get;
            set;
        }
    }

    public class SpellSlotMaximumInput
    {
        public int Level { get; set; }

        public int MaximumSlots { get; set; }
    }

    public class SpellEffectInput
    {
        public SpellEffectType EffectType
        {
            get;
            set;
        }

        public string Expression
        {
            get;
            set;
        } = string.Empty;

        public string? DamageType
        {
            get;
            set;
        }

        public bool AddsSpellcastingAbilityModifier
        {
            get;
            set;
        }

        public int AdditionalBonus
        {
            get;
            set;
        }

        public string? UpcastExpressionPerLevel
        {
            get;
            set;
        }

        public string? CantripScalingExpression
        {
            get;
            set;
        }
    }
}