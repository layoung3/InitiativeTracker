using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace InitiativeTracker.Web.Services;

public class CharacterService
{
    private readonly object _lock =
        new();

    private readonly IDbContextFactory<InitiativeTrackerDbContext>
        _dbContextFactory;

    public CharacterService(
        IDbContextFactory<InitiativeTrackerDbContext> dbContextFactory)
    {
        _dbContextFactory =
            dbContextFactory;
    }

    public IReadOnlyList<PlayerCharacter>
        GetCharacters(Guid playerProfileId)
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        return db.PlayerCharacters
            .AsNoTracking()
            .Where(x =>
                x.PlayerProfileId == playerProfileId &&
                x.IsActive)
            .OrderBy(x => x.Name)
            .ToList();
    }

    public PlayerCharacter?
        GetCharacter(Guid characterId)
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        return db.PlayerCharacters
            .AsNoTracking()
            .SingleOrDefault(
                x => x.Id == characterId &&
                     x.IsActive);
    }

    public PlayerCharacter CreateCharacter(
        Guid playerProfileId,
        string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Character name is required.",
                nameof(name));
        }

        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var now =
                DateTime.UtcNow;

            var character =
                new PlayerCharacter
                {
                    PlayerProfileId =
                        playerProfileId,

                    Name =
                        name.Trim(),

                    CreatedAtUtc =
                        now,

                    UpdatedAtUtc =
                        now
                };

            db.PlayerCharacters.Add(character);

            db.SaveChanges();

            EnsureCharacterSkills(db, character.Id);

            return character;
        }
    }

    public void UpdateCharacter(
        PlayerCharacter character)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var existing =
                db.PlayerCharacters
                    .SingleOrDefault(
                        x => x.Id == character.Id)
                ?? throw new ArgumentException(
                    "Character was not found.");

            existing.Name =
                character.Name.Trim();

            existing.Level =
                Math.Max(1, character.Level);

            existing.ProficiencyBonus =
                character.ProficiencyBonus;

            existing.MovementSpeed =
                Math.Max(
                    0,
                    character.MovementSpeed);

            existing.MaxHp =
                Math.Max(0, character.MaxHp);

            existing.CurrentHp =
                Math.Clamp(
                    character.CurrentHp,
                    0,
                    existing.MaxHp);

            existing.TemporaryHp =
                Math.Max(
                    0,
                    character.TemporaryHp);

            existing.Inspiration =
                character.Inspiration;

            existing.Strength =
                Math.Max(1, character.Strength);

            existing.Dexterity =
                Math.Max(1, character.Dexterity);

            existing.Constitution =
                Math.Max(1, character.Constitution);

            existing.Intelligence =
                Math.Max(1, character.Intelligence);

            existing.Wisdom =
                Math.Max(1, character.Wisdom);

            existing.Charisma =
                Math.Max(1, character.Charisma);

            existing.UpdatedAtUtc =
                DateTime.UtcNow;

            db.SaveChanges();
        }
    }

    public void UpdateCombatValues(
        Guid characterId,
        int currentHp,
        int maxHp,
        int temporaryHp)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var character =
                db.PlayerCharacters
                    .SingleOrDefault(
                        x => x.Id == characterId)
                ?? throw new ArgumentException(
                    "Character was not found.",
                    nameof(characterId));

            character.MaxHp =
                Math.Max(
                    0,
                    maxHp);

            character.CurrentHp =
                Math.Clamp(
                    currentHp,
                    0,
                    character.MaxHp);

            character.TemporaryHp =
                Math.Max(
                    0,
                    temporaryHp);

            character.UpdatedAtUtc =
                DateTime.UtcNow;

            db.SaveChanges();
        }
    }

    public void PermanentlyDeleteCharacter(
        Guid characterId)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var character =
                db.PlayerCharacters
                    .SingleOrDefault(
                        x => x.Id == characterId);

            if (character is null)
            {
                return;
            }

            db.PlayerCharacters.Remove(
                character);

            db.SaveChanges();
        }
    }

    public IReadOnlyList<PlayerCharacter> GetCharactersForProfiles(
        IReadOnlyCollection<Guid> profileIds)
    {
        if (profileIds.Count == 0)
        {
            return [];
        }

        using var db =
            _dbContextFactory.CreateDbContext();

        return db.PlayerCharacters
            .AsNoTracking()
            .Where(x =>
                profileIds.Contains(
                    x.PlayerProfileId) &&
                x.IsActive)
            .OrderBy(x => x.Name)
            .ToList();
    }

    public IReadOnlyList<PlayerCharacterSkill>GetSkills(Guid characterId)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory
                    .CreateDbContext();

            var characterExists =
                db.PlayerCharacters
                    .Any(
                        x =>
                            x.Id ==
                            characterId);

            if (!characterExists)
            {
                throw new ArgumentException(
                    "Character was not found.",
                    nameof(characterId));
            }

            EnsureCharacterSkills(
                db,
                characterId);

            return db.PlayerCharacterSkills
                .AsNoTracking()
                .Where(
                    x =>
                        x.PlayerCharacterId ==
                        characterId)
                .OrderBy(
                    x => x.Skill)
                .ToList();
        }
    }

    public void UpdateSkill(
        Guid characterId,
        CharacterSkillType skill,
        SkillProficiencyLevel proficiencyLevel,
        int additionalBonus,
        int? overrideBonus)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory
                    .CreateDbContext();

            var characterExists =
                db.PlayerCharacters
                    .Any(
                        x =>
                            x.Id ==
                            characterId);

            if (!characterExists)
            {
                throw new ArgumentException(
                    "Character was not found.",
                    nameof(characterId));
            }

            EnsureCharacterSkills(
                db,
                characterId);

            var characterSkill =
                db.PlayerCharacterSkills
                    .Single(
                        x =>
                            x.PlayerCharacterId ==
                                characterId &&
                            x.Skill ==
                                skill);

            characterSkill.ProficiencyLevel =
                proficiencyLevel;

            characterSkill.AdditionalBonus =
                additionalBonus;

            characterSkill.OverrideBonus =
                overrideBonus;

            db.SaveChanges();
        }
    }

    private static void EnsureCharacterSkills(
        InitiativeTrackerDbContext db,
        Guid characterId)
    {
        var existingSkills =
            db.PlayerCharacterSkills
                .Where(
                    x =>
                        x.PlayerCharacterId ==
                        characterId)
                .Select(
                    x => x.Skill)
                .ToHashSet();

        var missingSkills =
            Enum.GetValues<CharacterSkillType>()
                .Where(
                    skill =>
                        !existingSkills.Contains(
                            skill))
                .Select(
                    skill =>
                        new PlayerCharacterSkill
                        {
                            PlayerCharacterId =
                                characterId,

                            Skill =
                                skill,

                            ProficiencyLevel =
                                SkillProficiencyLevel.None,

                            AdditionalBonus =
                                0,

                            OverrideBonus =
                                null
                        })
                .ToList();

        if (missingSkills.Count == 0)
        {
            return;
        }

        db.PlayerCharacterSkills
            .AddRange(
                missingSkills);

        db.SaveChanges();
    }

    public void UpdateCharacterSheet(
        Guid characterId,
        PlayerCharacter values,
        IReadOnlyCollection<PlayerCharacterSkill> skillValues,
        bool updateHitPoints)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory
                    .CreateDbContext();

            var character =
                db.PlayerCharacters
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                            characterId)
                ?? throw new ArgumentException(
                    "Character was not found.",
                    nameof(characterId));

            if (string.IsNullOrWhiteSpace(
                    values.Name))
            {
                throw new ArgumentException(
                    "Character name is required.");
            }

            EnsureCharacterSkills(
                db,
                characterId);


            character.Name =
                values.Name.Trim();

            character.Level =
                Math.Max(
                    1,
                    values.Level);

            character.MovementSpeed =
                Math.Max(
                    0,
                    values.MovementSpeed);

            character.ProficiencyBonus =
                Math.Max(
                    0,
                    values.ProficiencyBonus);

            character.Inspiration =
                values.Inspiration;


            character.Strength =
                Math.Max(
                    1,
                    values.Strength);

            character.Dexterity =
                Math.Max(
                    1,
                    values.Dexterity);

            character.Constitution =
                Math.Max(
                    1,
                    values.Constitution);

            character.Intelligence =
                Math.Max(
                    1,
                    values.Intelligence);

            character.Wisdom =
                Math.Max(
                    1,
                    values.Wisdom);

            character.Charisma =
                Math.Max(
                    1,
                    values.Charisma);


            character.StrengthSaveProficient =
                values.StrengthSaveProficient;

            character.DexteritySaveProficient =
                values.DexteritySaveProficient;

            character.ConstitutionSaveProficient =
                values.ConstitutionSaveProficient;

            character.IntelligenceSaveProficient =
                values.IntelligenceSaveProficient;

            character.WisdomSaveProficient =
                values.WisdomSaveProficient;

            character.CharismaSaveProficient =
                values.CharismaSaveProficient;


            /*
             * Do not overwrite persistent HP from the edit
             * page while this character is participating in
             * the current encounter.
             *
             * Live combat HP remains authoritative then.
             */
            if (updateHitPoints)
            {
                character.MaxHp =
                    Math.Max(
                        0,
                        values.MaxHp);

                character.CurrentHp =
                    Math.Clamp(
                        values.CurrentHp,
                        0,
                        character.MaxHp);

                character.TemporaryHp =
                    Math.Max(
                        0,
                        values.TemporaryHp);
            }


            var existingSkills =
                db.PlayerCharacterSkills
                    .Where(
                        x =>
                            x.PlayerCharacterId ==
                            characterId)
                    .ToDictionary(
                        x => x.Skill);


            foreach (var skillValue
                     in skillValues)
            {
                if (!existingSkills.TryGetValue(
                        skillValue.Skill,
                        out var skill))
                {
                    continue;
                }

                skill.ProficiencyLevel =
                    skillValue.ProficiencyLevel;

                skill.AdditionalBonus =
                    skillValue.AdditionalBonus;

                skill.OverrideBonus =
                    skillValue.OverrideBonus;
            }


            character.UpdatedAtUtc =
                DateTime.UtcNow;

            db.SaveChanges();
        }
    }
}