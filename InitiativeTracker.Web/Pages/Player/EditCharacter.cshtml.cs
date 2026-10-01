using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Data.Enums;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.Player;

[Authorize(Roles = "Player")]
public class EditCharacterModel : PageModel
{
    private readonly CharacterService
        _characterService;

    private readonly EncounterStateService
        _encounterState;


    public EditCharacterModel(
        CharacterService characterService,
        EncounterStateService encounterState)
    {
        _characterService =
            characterService;

        _encounterState =
            encounterState;
    }


    [BindProperty]
    public CharacterEditInput Input
    {
        get;
        set;
    } = new();


    [BindProperty]
    public List<SkillEditInput>
    SkillInputs
    {
        get;
        set;
    } = [];


    public bool IsInEncounter
    {
        get;
        private set;
    }

    public IActionResult OnGet(
        Guid id)
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


        Input =
            new CharacterEditInput
            {
                Id = character.Id,

                Name = character.Name,

                Level = character.Level,

                MovementSpeed = character.MovementSpeed,

                ProficiencyBonus = character.ProficiencyBonus,

                ArmorClass = character.ArmorClass,

                CurrentHp = character.CurrentHp,

                MaxHp = character.MaxHp,

                TemporaryHp = character.TemporaryHp,

                Inspiration = character.Inspiration,

                Strength = character.Strength,

                Dexterity = character.Dexterity,

                Constitution = character.Constitution,

                Intelligence = character.Intelligence,

                Wisdom = character.Wisdom,

                Charisma = character.Charisma,

                StrengthSaveProficient = character.StrengthSaveProficient,

                DexteritySaveProficient = character.DexteritySaveProficient,

                ConstitutionSaveProficient = character.ConstitutionSaveProficient,

                IntelligenceSaveProficient = character.IntelligenceSaveProficient,

                WisdomSaveProficient = character.WisdomSaveProficient,

                CharismaSaveProficient = character.CharismaSaveProficient
            };


        SkillInputs =
            _characterService
                .GetSkills(
                    character.Id)
                .OrderBy(
                    x =>
                        GetSkillName(
                            x.Skill))
                .Select(
                    x =>
                        new SkillEditInput
                        {
                            Skill =
                                x.Skill,

                            ProficiencyLevel =
                                x.ProficiencyLevel,

                            AdditionalBonus =
                                x.AdditionalBonus,

                            OverrideBonus =
                                x.OverrideBonus
                        })
                .ToList();


        SetEncounterState(
            character.Id);


        return Page();
    }


    public IActionResult OnPost()
    {
        if (!TryGetPlayerProfileId(
                out var profileId))
        {
            return Unauthorized();
        }


        var character =
            _characterService
                .GetCharacter(
                    Input.Id);

        if (character is null)
        {
            return NotFound();
        }


        if (character.PlayerProfileId !=
            profileId)
        {
            return Forbid();
        }


        SetEncounterState(
            character.Id);


        if (string.IsNullOrWhiteSpace(
                Input.Name))
        {
            ModelState.AddModelError(
                "Input.Name",
                "Character name is required.");
        }


        if (!ModelState.IsValid)
        {
            return Page();
        }


        var values =
            new PlayerCharacter
            {
                Name =
                    Input.Name,

                Level =
                    Input.Level,

                MovementSpeed =
                    Input.MovementSpeed,

                ProficiencyBonus =
                    Input.ProficiencyBonus,

                ArmorClass = Input.ArmorClass,

                CurrentHp =
                    Input.CurrentHp,

                MaxHp =
                    Input.MaxHp,

                TemporaryHp =
                    Input.TemporaryHp,

                Inspiration =
                    Input.Inspiration,

                Strength =
                    Input.Strength,

                Dexterity =
                    Input.Dexterity,

                Constitution =
                    Input.Constitution,

                Intelligence =
                    Input.Intelligence,

                Wisdom =
                    Input.Wisdom,

                Charisma =
                    Input.Charisma,

                StrengthSaveProficient =
                    Input.StrengthSaveProficient,

                DexteritySaveProficient =
                    Input.DexteritySaveProficient,

                ConstitutionSaveProficient =
                    Input.ConstitutionSaveProficient,

                IntelligenceSaveProficient =
                    Input.IntelligenceSaveProficient,

                WisdomSaveProficient =
                    Input.WisdomSaveProficient,

                CharismaSaveProficient =
                    Input.CharismaSaveProficient
            };

        var skillValues =
            SkillInputs
                .Select(
                    x =>
                        new PlayerCharacterSkill
                        {
                            Skill =
                                x.Skill,

                            ProficiencyLevel =
                                x.ProficiencyLevel,

                            AdditionalBonus =
                                x.AdditionalBonus,

                            OverrideBonus =
                                x.OverrideBonus
                        })
                .ToList();


        _characterService
            .UpdateCharacterSheet(
                character.Id,
                values,
                skillValues,
                updateHitPoints:
                    !IsInEncounter);


        return RedirectToPage(
            "/Player/Character",
            new
            {
                id =
                    character.Id
            });
    }


    public string GetSkillName(
        CharacterSkillType skill)
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


    public string GetSkillAbilityName(
        CharacterSkillType skill)
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

            _ =>
                "—"
        };
    }


    private void SetEncounterState(
        Guid characterId)
    {
        IsInEncounter =
            _encounterState
                .CurrentEncounter
                .Combatants
                .Any(
                    x =>
                        x.PlayerCharacterId ==
                        characterId);
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


    public class CharacterEditInput
    {
        public Guid Id { get; set; }

        public string Name { get; set; } =
            string.Empty;

        public int Level { get; set; } = 1;

        public int MovementSpeed { get; set; } = 30;

        public int ProficiencyBonus { get; set; } = 2;

        public int ArmorClass { get; set; } = 10;

        public int CurrentHp { get; set; }

        public int MaxHp { get; set; }

        public int TemporaryHp { get; set; }


        public bool Inspiration { get; set; }


        public int Strength { get; set; } = 10;
        public int Dexterity { get; set; } = 10;
        public int Constitution { get; set; } = 10;
        public int Intelligence { get; set; } = 10;
        public int Wisdom { get; set; } = 10;
        public int Charisma { get; set; } = 10;


        public bool StrengthSaveProficient
        {
            get;
            set;
        }

        public bool DexteritySaveProficient
        {
            get;
            set;
        }

        public bool ConstitutionSaveProficient
        {
            get;
            set;
        }

        public bool IntelligenceSaveProficient
        {
            get;
            set;
        }

        public bool WisdomSaveProficient
        {
            get;
            set;
        }

        public bool CharismaSaveProficient
        {
            get;
            set;
        }
    }

    public class SkillEditInput
    {
        public CharacterSkillType Skill
        {
            get;
            set;
        }

        public SkillProficiencyLevel ProficiencyLevel
        {
            get;
            set;
        }

        public int AdditionalBonus
        {
            get;
            set;
        }

        public int? OverrideBonus
        {
            get;
            set;
        }
    }
}