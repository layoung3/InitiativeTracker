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

    public string? OpenEditor{ get; private set; }

    public PlayerCharacter Character { get; private set; } = null!;

    public IReadOnlyList<PlayerCharacterSkill> Skills { get; private set; } = [];

    public IReadOnlyList<PlayerCharacterItem> InventoryItems { get; private set; } = [];

    public IReadOnlyDictionary<Guid, PlayerCharacterWeapon> WeaponsByItemId { get; private set; } = new Dictionary<Guid, PlayerCharacterWeapon>();

    public IReadOnlyList<Dnd5eWeaponDefinition> StandardWeapons => Dnd5eWeaponCatalog.StandardWeapons;

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


    public IActionResult OnGet(Guid id, string? editor=null, string? modal=null)
    {
        OpenEditor = editor;
        OpenModal = modal;

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

        WeaponsByItemId =
            _characterService
                .GetWeapons(
                    character.Id)
                .ToDictionary(
                    x => x.PlayerCharacterItemId);

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
}