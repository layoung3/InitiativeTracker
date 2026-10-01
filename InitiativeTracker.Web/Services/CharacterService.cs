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

            existing.ArmorClass =
                Math.Max(
                    0,
                    character.ArmorClass);

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

            character.ArmorClass =
                Math.Max(
                    0,
                    values.ArmorClass);

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

    public void AdjustCharacterHp(
        Guid characterId,
        int amount)
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

            if (amount < 0)
            {
                var damage =
                    Math.Abs(amount);

                if (character.TemporaryHp > 0)
                {
                    var absorbed =
                        Math.Min(
                            character.TemporaryHp,
                            damage);

                    character.TemporaryHp -=
                        absorbed;

                    damage -=
                        absorbed;
                }

                if (damage > 0)
                {
                    character.CurrentHp =
                        Math.Max(
                            0,
                            character.CurrentHp -
                            damage);
                }
            }
            else
            {
                character.CurrentHp =
                    Math.Min(
                        character.MaxHp,
                        character.CurrentHp +
                        amount);
            }

            character.UpdatedAtUtc =
                DateTime.UtcNow;

            db.SaveChanges();
        }
    }

    public void SetCharacterCurrentHp(
        Guid characterId,
        int currentHp)
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

            character.CurrentHp =
                Math.Clamp(
                    currentHp,
                    0,
                    character.MaxHp);

            character.UpdatedAtUtc =
                DateTime.UtcNow;

            db.SaveChanges();
        }
    }

    public void SetCharacterTemporaryHp(
        Guid characterId,
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

            character.TemporaryHp =
                Math.Max(
                    0,
                    temporaryHp);

            character.UpdatedAtUtc =
                DateTime.UtcNow;

            db.SaveChanges();
        }
    }

    public void SetCharacterInspiration(
        Guid characterId,
        bool inspiration)
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

            character.Inspiration =
                inspiration;

            character.UpdatedAtUtc =
                DateTime.UtcNow;

            db.SaveChanges();
        }
    }

    public void SetCharacterExhaustion(
        Guid characterId,
        int exhaustionLevel)
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

            character.ExhaustionLevel =
                Math.Clamp(
                    exhaustionLevel,
                    0,
                    6);

            character.UpdatedAtUtc =
                DateTime.UtcNow;

            db.SaveChanges();
        }
    }

    public IReadOnlyList<PlayerCharacterItem> GetInventory(
        Guid characterId)
    {
        using var db =
            _dbContextFactory.CreateDbContext();

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

        var items =
            db.PlayerCharacterItems
                .AsNoTracking()
                .Where(
                    x =>
                        x.PlayerCharacterId ==
                        characterId)
                .ToList();

        return items
            .OrderBy(
                x =>
                    GetInventoryTypeSortOrder(
                        x.ItemType))
            .ThenByDescending(
                x => x.IsEquipped)
            .ThenBy(
                x => x.Name)
            .ToList();
    }


    public PlayerCharacterItem AddInventoryItem(
        Guid characterId,
        string name,
        CharacterItemType itemType,
        int quantity,
        bool isEquipped,
        string? notes = null)
    {
        var normalized =
            NormalizeInventoryItemInput(
                name,
                notes);

        if (!Enum.IsDefined(
                typeof(CharacterItemType),
                itemType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(itemType));
        }

        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Quantity must be at least 1.");
        }

        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var character =
                db.PlayerCharacters
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                            characterId)
                ?? throw new ArgumentException(
                    "Character was not found.",
                    nameof(characterId));

            var now =
                DateTime.UtcNow;

            var item =
                new PlayerCharacterItem
                {
                    PlayerCharacterId =
                        characterId,

                    Name =
                        normalized.Name,

                    ItemType =
                        itemType,

                    Quantity =
                        quantity,

                    IsEquipped =
                        CanEquipInventoryItemType(
                            itemType) &&
                        isEquipped,

                    Notes =
                        normalized.Notes,

                    CreatedAtUtc =
                        now,

                    UpdatedAtUtc =
                        now
                };

            db.PlayerCharacterItems.Add(
                item);

            character.UpdatedAtUtc =
                now;

            db.SaveChanges();

            return item;
        }
    }


    public void UpdateInventoryItem(
        Guid characterId,
        Guid itemId,
        string name,
        CharacterItemType itemType,
        int quantity,
        bool isEquipped,
        string? notes = null)
    {
        var normalized =
            NormalizeInventoryItemInput(
                name,
                notes);

        if (!Enum.IsDefined(
                typeof(CharacterItemType),
                itemType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(itemType));
        }

        if (quantity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Quantity must be at least 1.");
        }

        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var character =
                db.PlayerCharacters
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                            characterId)
                ?? throw new ArgumentException(
                    "Character was not found.",
                    nameof(characterId));

            var item =
                db.PlayerCharacterItems
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                                itemId &&
                            x.PlayerCharacterId ==
                                characterId);

            if (item is null)
            {
                throw new ArgumentException(
                    "Inventory item was not found.",
                    nameof(itemId));
            }

            var now =
                DateTime.UtcNow;

            item.Name =
                normalized.Name;

            item.ItemType =
                itemType;

            item.Quantity =
                quantity;

            item.IsEquipped =
                CanEquipInventoryItemType(
                    itemType) &&
                isEquipped;

            item.Notes =
                normalized.Notes;

            item.UpdatedAtUtc =
                now;

            character.UpdatedAtUtc =
                now;

            if (itemType != CharacterItemType.Weapon)
            {
                var existingWeapon =
                    db.PlayerCharacterWeapons
                        .SingleOrDefault(
                            x =>
                                x.PlayerCharacterItemId ==
                                itemId);

                if (existingWeapon is not null)
                {
                    db.PlayerCharacterWeapons
                        .Remove(
                            existingWeapon);
                }
            }

            db.SaveChanges();
        }
    }


    public void ToggleInventoryItemEquipped(
        Guid characterId,
        Guid itemId)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var character =
                db.PlayerCharacters
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                            characterId)
                ?? throw new ArgumentException(
                    "Character was not found.",
                    nameof(characterId));

            var item =
                db.PlayerCharacterItems
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                                itemId &&
                            x.PlayerCharacterId ==
                                characterId);

            if (item is null)
            {
                throw new ArgumentException(
                    "Inventory item was not found.",
                    nameof(itemId));
            }

            if (!CanEquipInventoryItemType(
                    item.ItemType))
            {
                throw new InvalidOperationException(
                    "This item type cannot be equipped.");
            }

            var now =
                DateTime.UtcNow;

            item.IsEquipped =
                !item.IsEquipped;

            item.UpdatedAtUtc =
                now;

            character.UpdatedAtUtc =
                now;

            db.SaveChanges();
        }
    }

    public void AdjustInventoryItemQuantity(
        Guid characterId,
        Guid itemId,
        int amount)
    {
        if (amount == 0)
        {
            return;
        }

        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var character =
                db.PlayerCharacters
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                            characterId)
                ?? throw new ArgumentException(
                    "Character was not found.",
                    nameof(characterId));

            var item =
                db.PlayerCharacterItems
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                                itemId &&
                            x.PlayerCharacterId ==
                                characterId);

            if (item is null)
            {
                throw new ArgumentException(
                    "Inventory item was not found.",
                    nameof(itemId));
            }

            item.Quantity =
                Math.Max(
                    1,
                    item.Quantity + amount);

            var now =
                DateTime.UtcNow;

            item.UpdatedAtUtc =
                now;

            character.UpdatedAtUtc =
                now;

            db.SaveChanges();
        }
    }


    public void DeleteInventoryItem(
        Guid characterId,
        Guid itemId)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var character =
                db.PlayerCharacters
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                            characterId)
                ?? throw new ArgumentException(
                    "Character was not found.",
                    nameof(characterId));

            var item =
                db.PlayerCharacterItems
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                                itemId &&
                            x.PlayerCharacterId ==
                                characterId);

            if (item is null)
            {
                return;
            }

            db.PlayerCharacterItems.Remove(
                item);

            character.UpdatedAtUtc =
                DateTime.UtcNow;

            db.SaveChanges();
        }
    }

    public IReadOnlyList<PlayerCharacterCondition> GetConditions(Guid characterId)
    {
        using var db =
            _dbContextFactory.CreateDbContext();

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

        return db.PlayerCharacterConditions
            .AsNoTracking()
            .Where(
                x =>
                    x.PlayerCharacterId ==
                    characterId)
            .OrderBy(
                x => x.Name)
            .ToList();
    }

    public PlayerCharacterCondition AddCondition(
        Guid characterId,
        string name,
        bool isCustom,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Condition name is required.",
                nameof(name));
        }

        var normalizedName =
            name.Trim();

        if (normalizedName.Length > 100)
        {
            throw new ArgumentException(
                "Condition name cannot exceed 100 characters.",
                nameof(name));
        }

        var normalizedNotes =
            string.IsNullOrWhiteSpace(notes)
                ? null
                : notes.Trim();

        if (normalizedNotes?.Length > 1000)
        {
            throw new ArgumentException(
                "Condition notes cannot exceed 1000 characters.",
                nameof(notes));
        }

        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var character =
                db.PlayerCharacters
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                            characterId)
                ?? throw new ArgumentException(
                    "Character was not found.",
                    nameof(characterId));

            var existingNames =
                db.PlayerCharacterConditions
                    .Where(
                        x =>
                            x.PlayerCharacterId ==
                            characterId)
                    .Select(
                        x => x.Name)
                    .ToList();

            if (existingNames.Any(
                    existing =>
                        string.Equals(
                            existing,
                            normalizedName,
                            StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"{normalizedName} is already active.");
            }

            var condition =
                new PlayerCharacterCondition
                {
                    PlayerCharacterId =
                        characterId,

                    Name =
                        normalizedName,

                    IsCustom =
                        isCustom,

                    Notes =
                        normalizedNotes
                };

            db.PlayerCharacterConditions
                .Add(condition);

            character.UpdatedAtUtc =
                DateTime.UtcNow;

            db.SaveChanges();

            return condition;
        }
    }

    public void RemoveCondition(
        Guid characterId,
        Guid conditionId)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var character =
                db.PlayerCharacters
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                            characterId)
                ?? throw new ArgumentException(
                    "Character was not found.",
                    nameof(characterId));

            var condition =
                db.PlayerCharacterConditions
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                                conditionId &&
                            x.PlayerCharacterId ==
                                characterId);

            if (condition is null)
            {
                return;
            }

            db.PlayerCharacterConditions
                .Remove(condition);

            character.UpdatedAtUtc =
                DateTime.UtcNow;

            db.SaveChanges();
        }
    }

    public void UpdateEncounterState(
        Guid characterId,
        int currentHp,
        int maxHp,
        int temporaryHp,
        IReadOnlyCollection<PlayerCharacterCondition> conditions)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var character =
                db.PlayerCharacters
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                            characterId)
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


            var existingConditions =
                db.PlayerCharacterConditions
                    .Where(
                        x =>
                            x.PlayerCharacterId ==
                            characterId)
                    .ToList();


            var incomingIds =
                conditions
                    .Select(
                        x => x.Id)
                    .ToHashSet();


            var removedConditions =
                existingConditions
                    .Where(
                        x =>
                            !incomingIds.Contains(
                                x.Id))
                    .ToList();

            if (removedConditions.Count > 0)
            {
                db.PlayerCharacterConditions
                    .RemoveRange(
                        removedConditions);
            }


            var existingById =
                existingConditions
                    .ToDictionary(
                        x => x.Id);


            foreach (var incoming
                     in conditions)
            {
                if (existingById.TryGetValue(
                        incoming.Id,
                        out var existing))
                {
                    existing.Name =
                        incoming.Name;

                    existing.IsCustom =
                        incoming.IsCustom;

                    existing.Notes =
                        incoming.Notes;

                    continue;
                }

                db.PlayerCharacterConditions
                    .Add(
                        new PlayerCharacterCondition
                        {
                            Id =
                                incoming.Id,

                            PlayerCharacterId =
                                characterId,

                            Name =
                                incoming.Name,

                            IsCustom =
                                incoming.IsCustom,

                            Notes =
                                incoming.Notes
                        });
            }


            character.UpdatedAtUtc =
                DateTime.UtcNow;

            db.SaveChanges();
        }
    }

    private static (string Name,string? Notes) NormalizeInventoryItemInput(
        string name,
        string? notes)
    {
        if (string.IsNullOrWhiteSpace(
                name))
        {
            throw new ArgumentException(
                "Item name is required.",
                nameof(name));
        }

        var normalizedName =
            name.Trim();

        if (normalizedName.Length > 150)
        {
            throw new ArgumentException(
                "Item name cannot exceed 150 characters.",
                nameof(name));
        }

        var normalizedNotes =
            string.IsNullOrWhiteSpace(
                notes)
                ? null
                : notes.Trim();

        if (normalizedNotes?.Length > 2000)
        {
            throw new ArgumentException(
                "Item notes cannot exceed 2000 characters.",
                nameof(notes));
        }

        return (
            normalizedName,
            normalizedNotes);
    }

    private static bool CanEquipInventoryItemType(
        CharacterItemType itemType)
    {
        return itemType is
            CharacterItemType.Weapon or
            CharacterItemType.Armor or
            CharacterItemType.Equipment;
    }


    private static int GetInventoryTypeSortOrder(
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

    public PlayerCharacterWeapon? GetWeapon(
        Guid characterId,
        Guid itemId)
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        return db.PlayerCharacterWeapons
            .AsNoTracking()
            .Include(
                x => x.Item)
            .Include(
                x => x.DamageComponents)
            .SingleOrDefault(
                x =>
                    x.PlayerCharacterItemId ==
                        itemId &&
                    x.Item.PlayerCharacterId ==
                        characterId);
    }

    public IReadOnlyList<PlayerCharacterWeapon> GetWeapons(
        Guid characterId)
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        return db.PlayerCharacterWeapons
            .AsNoTracking()
            .Include(
                x => x.Item)
            .Include(
                x => x.DamageComponents)
            .Where(
                x =>
                    x.Item.PlayerCharacterId ==
                    characterId)
            .OrderBy(
                x => x.Item.Name)
            .ToList();
    }

    public void SaveWeapon(
        Guid characterId,
        Guid itemId,
        PlayerCharacterWeapon values)
    {
        if (!Enum.IsDefined(
                typeof(CharacterAbilityType),
                values.AttackAbility))
        {
            throw new ArgumentOutOfRangeException(
                nameof(values.AttackAbility));
        }


        if (values.NormalRange.HasValue &&
            values.NormalRange.Value < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(values.NormalRange),
                "Normal range must be greater than zero.");
        }


        if (values.LongRange.HasValue &&
            values.LongRange.Value < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(values.LongRange),
                "Long range must be greater than zero.");
        }


        if (values.LongRange.HasValue &&
            values.NormalRange.HasValue &&
            values.LongRange.Value <
                values.NormalRange.Value)
        {
            throw new ArgumentException(
                "Long range cannot be shorter than normal range.");
        }


        var damageComponents =
            values.DamageComponents
                .OrderBy(
                    x => x.SortOrder)
                .ToList();


        if (damageComponents.Count == 0)
        {
            throw new ArgumentException(
                "A weapon must have at least one damage component.");
        }


        if (damageComponents.Count(
                x => x.IsPrimary) != 1)
        {
            throw new ArgumentException(
                "A weapon must have exactly one primary damage component.");
        }


        foreach (var component
                 in damageComponents)
        {
            ValidateWeaponDamageComponent(
                component);
        }


        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();


            var character =
                db.PlayerCharacters
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                            characterId)
                ?? throw new ArgumentException(
                    "Character was not found.",
                    nameof(characterId));


            var item =
                db.PlayerCharacterItems
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                                itemId &&
                            x.PlayerCharacterId ==
                                characterId)
                ?? throw new ArgumentException(
                    "Inventory item was not found.",
                    nameof(itemId));


            var weapon =
                db.PlayerCharacterWeapons
                    .SingleOrDefault(
                        x =>
                            x.PlayerCharacterItemId ==
                            itemId);


            if (weapon is null)
            {
                weapon =
                    new PlayerCharacterWeapon
                    {
                        PlayerCharacterItemId =
                            itemId
                    };

                db.PlayerCharacterWeapons
                    .Add(
                        weapon);
            }


            weapon.AttackAbility =
                values.AttackAbility;

            weapon.UsesProficiency =
                values.UsesProficiency;

            weapon.AttackBonus =
                values.AttackBonus;

            weapon.PrimaryDamageBonus =
                values.PrimaryDamageBonus;

            weapon.NormalRange =
                values.NormalRange;

            weapon.LongRange =
                values.LongRange;


            /*
             * Replace the weapon's damage components directly
             * through their DbSet rather than replacing the
             * tracked navigation collection.
             */
            var existingDamageComponents =
                db.PlayerCharacterWeaponDamageComponents
                    .Where(
                        x =>
                            x.PlayerCharacterItemId ==
                            itemId)
                    .ToList();


            if (existingDamageComponents.Count > 0)
            {
                db.PlayerCharacterWeaponDamageComponents
                    .RemoveRange(
                        existingDamageComponents);
            }


            var newDamageComponents =
                damageComponents
                    .Select(
                        x =>
                            new PlayerCharacterWeaponDamageComponent
                            {
                                PlayerCharacterItemId =
                                    itemId,

                                DamageDice =
                                    x.DamageDice.Trim(),

                                DamageType =
                                    x.DamageType.Trim(),

                                IsPrimary =
                                    x.IsPrimary,

                                AddsAbilityModifier =
                                    x.AddsAbilityModifier,

                                VersatileDamageDice =
                                    string.IsNullOrWhiteSpace(
                                        x.VersatileDamageDice)
                                        ? null
                                        : x.VersatileDamageDice.Trim(),

                                SortOrder =
                                    x.SortOrder
                            })
                    .ToList();


            db.PlayerCharacterWeaponDamageComponents
                .AddRange(
                    newDamageComponents);


            item.ItemType =
                CharacterItemType.Weapon;


            var now =
                DateTime.UtcNow;

            item.UpdatedAtUtc =
                now;

            character.UpdatedAtUtc =
                now;


            db.SaveChanges();
        }
    }

    private static void ValidateWeaponDamageComponent(
        PlayerCharacterWeaponDamageComponent component)
    {
        if (string.IsNullOrWhiteSpace(
                component.DamageDice))
        {
            throw new ArgumentException(
                "Damage dice are required.");
        }


        if (component.DamageDice.Trim().Length > 50)
        {
            throw new ArgumentException(
                "Damage dice cannot exceed 50 characters.");
        }


        if (string.IsNullOrWhiteSpace(
                component.DamageType))
        {
            throw new ArgumentException(
                "Damage type is required.");
        }


        if (component.DamageType.Trim().Length > 100)
        {
            throw new ArgumentException(
                "Damage type cannot exceed 100 characters.");
        }


        if (!string.IsNullOrWhiteSpace(
                component.VersatileDamageDice) &&
            component.VersatileDamageDice
                .Trim()
                .Length > 50)
        {
            throw new ArgumentException(
                "Versatile damage dice cannot exceed 50 characters.");
        }
    }
}