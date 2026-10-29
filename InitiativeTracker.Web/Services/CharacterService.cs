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

    public IReadOnlyList<PlayerCharacterSkill> GetSkills(Guid characterId)
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
        IReadOnlyCollection<PlayerCharacterCondition> conditions,
        int exhaustionLevel)
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


            character.ExhaustionLevel = Math.Clamp(exhaustionLevel, 0, 6);

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

    private static (string Name, string? Notes) NormalizeInventoryItemInput(
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

    public IReadOnlyList<PlayerCharacterAttack> GetCharacterAttacks(
        Guid characterId)
    {
        using var db =
            _dbContextFactory.CreateDbContext();


        return db.PlayerCharacterAttacks
            .AsNoTracking()
            .Include(
                x => x.DamageComponents)
            .Where(
                x =>
                    x.PlayerCharacterId ==
                    characterId)
            .OrderBy(
                x => x.SortOrder)
            .ThenBy(
                x => x.Name)
            .ToList();
    }

    public PlayerCharacterAttack SaveCharacterAttack(
        Guid characterId,
        Guid? attackId,
        PlayerCharacterAttack values)
    {
        if (string.IsNullOrWhiteSpace(
                values.Name))
        {
            throw new ArgumentException(
                "Attack name is required.");
        }


        var normalizedName =
            values.Name.Trim();


        if (normalizedName.Length > 150)
        {
            throw new ArgumentException(
                "Attack name cannot exceed 150 characters.");
        }


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


        if (values.NormalRange.HasValue &&
            values.LongRange.HasValue &&
            values.LongRange.Value <
                values.NormalRange.Value)
        {
            throw new ArgumentException(
                "Long range cannot be shorter than normal range.");
        }


        var normalizedNotes =
            string.IsNullOrWhiteSpace(
                values.Notes)
                ? null
                : values.Notes.Trim();


        if (normalizedNotes?.Length > 2000)
        {
            throw new ArgumentException(
                "Attack notes cannot exceed 2000 characters.");
        }


        var damageComponents =
            values.DamageComponents
                .OrderBy(
                    x => x.SortOrder)
                .ToList();


        foreach (var component
                 in damageComponents)
        {
            ValidateCharacterAttackDamageComponent(
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


            PlayerCharacterAttack attack;


            if (attackId.HasValue &&
                attackId.Value != Guid.Empty)
            {
                attack =
                    db.PlayerCharacterAttacks
                        .SingleOrDefault(
                            x =>
                                x.Id ==
                                    attackId.Value &&
                                x.PlayerCharacterId ==
                                    characterId)
                    ?? throw new ArgumentException(
                        "Attack was not found.",
                        nameof(attackId));
            }
            else
            {
                var nextSortOrder =
                    db.PlayerCharacterAttacks
                        .Where(
                            x =>
                                x.PlayerCharacterId ==
                                    characterId)
                        .Select(
                            x => (int?)x.SortOrder)
                        .Max() ?? -1;


                attack =
                    new PlayerCharacterAttack
                    {
                        Id =
                            Guid.NewGuid(),

                        PlayerCharacterId =
                            characterId,

                        SortOrder =
                            nextSortOrder + 1,

                        IsEnabled =
                            true,

                        CreatedAtUtc =
                            DateTime.UtcNow
                    };


                db.PlayerCharacterAttacks
                    .Add(
                        attack);
            }


            attack.Name =
                normalizedName;

            attack.AttackAbility =
                values.AttackAbility;

            attack.AddsAbilityModifierToHit =
                values.AddsAbilityModifierToHit;

            attack.UsesProficiency =
                values.UsesProficiency;

            attack.AttackBonus =
                values.AttackBonus;

            attack.NormalRange =
                values.NormalRange;

            attack.LongRange =
                values.LongRange;

            attack.Notes =
                normalizedNotes;

            attack.UpdatedAtUtc =
                DateTime.UtcNow;


            var existingDamage =
                db.PlayerCharacterAttackDamageComponents
                    .Where(
                        x =>
                            x.PlayerCharacterAttackId ==
                                attack.Id)
                    .ToList();


            if (existingDamage.Count > 0)
            {
                db.PlayerCharacterAttackDamageComponents
                    .RemoveRange(
                        existingDamage);
            }


            var newDamage =
                damageComponents
                    .Select(
                        x =>
                            new PlayerCharacterAttackDamageComponent
                            {
                                Id =
                                    Guid.NewGuid(),

                                PlayerCharacterAttackId =
                                    attack.Id,

                                DamageExpression =
                                    x.DamageExpression.Trim(),

                                DamageType =
                                    x.DamageType.Trim(),

                                AddsAbilityModifier =
                                    x.AddsAbilityModifier,

                                AdditionalDamageBonus =
                                    x.AdditionalDamageBonus,

                                SortOrder =
                                    x.SortOrder
                            })
                    .ToList();


            if (newDamage.Count > 0)
            {
                db.PlayerCharacterAttackDamageComponents
                    .AddRange(
                        newDamage);
            }


            character.UpdatedAtUtc =
                DateTime.UtcNow;


            db.SaveChanges();


            return attack;
        }
    }

    public void DeleteCharacterAttack(
        Guid characterId,
        Guid attackId)
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


            var attack =
                db.PlayerCharacterAttacks
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                                attackId &&
                            x.PlayerCharacterId ==
                                characterId);


            if (attack is null)
            {
                return;
            }


            db.PlayerCharacterAttacks
                .Remove(
                    attack);


            character.UpdatedAtUtc =
                DateTime.UtcNow;


            db.SaveChanges();
        }
    }

    private static void ValidateCharacterAttackDamageComponent(
        PlayerCharacterAttackDamageComponent component)
    {
        if (string.IsNullOrWhiteSpace(
                component.DamageExpression))
        {
            throw new ArgumentException(
                "Damage is required.");
        }


        if (component.DamageExpression
                .Trim()
                .Length > 50)
        {
            throw new ArgumentException(
                "Damage cannot exceed 50 characters.");
        }


        if (string.IsNullOrWhiteSpace(
                component.DamageType))
        {
            throw new ArgumentException(
                "Damage type is required.");
        }


        if (component.DamageType
                .Trim()
                .Length > 100)
        {
            throw new ArgumentException(
                "Damage type cannot exceed 100 characters.");
        }
    }

    public PlayerCharacterSpellcasting? GetSpellcasting(
        Guid characterId)
    {
        using var db =
            _dbContextFactory.CreateDbContext();


        return db.PlayerCharacterSpellcastings
            .AsNoTracking()
            .Include(
                x => x.SpellSlots)
            .SingleOrDefault(
                x =>
                    x.PlayerCharacterId ==
                    characterId);
    }

    public IReadOnlyList<PlayerCharacterSpell> GetSpells(
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

        return db.PlayerCharacterSpells
            .AsNoTracking()
            .Include(
                x => x.EffectComponents)
            .Where(
                x =>
                    x.PlayerCharacterId ==
                    characterId)
            .OrderBy(
                x => x.Level)
            .ThenBy(
                x => x.SortOrder)
            .ThenBy(
                x => x.Name)
            .ToList();
    }

    public void SaveSpellcasting(
        Guid characterId,
        CharacterAbilityType ability,
        int additionalSpellAttackBonus,
        int additionalSpellSaveDcBonus,
        IReadOnlyDictionary<int, int> maximumSlotsByLevel)
    {
        if (!Enum.IsDefined(
                typeof(CharacterAbilityType),
                ability))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ability));
        }


        foreach (var pair
                 in maximumSlotsByLevel)
        {
            if (pair.Key < 1 ||
                pair.Key > 9)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumSlotsByLevel),
                    "Spell slot levels must be between 1 and 9.");
            }


            if (pair.Value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumSlotsByLevel),
                    "Maximum spell slots cannot be negative.");
            }
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


            var spellcasting =
                db.PlayerCharacterSpellcastings
                    .Include(
                        x => x.SpellSlots)
                    .SingleOrDefault(
                        x =>
                            x.PlayerCharacterId ==
                            characterId);


            var now =
                DateTime.UtcNow;


            if (spellcasting is null)
            {
                spellcasting =
                    new PlayerCharacterSpellcasting
                    {
                        Id =
                            Guid.NewGuid(),

                        PlayerCharacterId =
                            characterId,

                        CreatedAtUtc =
                            now
                    };


                db.PlayerCharacterSpellcastings
                    .Add(
                        spellcasting);
            }


            spellcasting.SpellcastingAbility =
                ability;

            spellcasting.AdditionalSpellAttackBonus =
                additionalSpellAttackBonus;

            spellcasting.AdditionalSpellSaveDcBonus =
                additionalSpellSaveDcBonus;

            spellcasting.UpdatedAtUtc =
                now;


            var existingByLevel =
                spellcasting.SpellSlots
                    .ToDictionary(
                        x => x.Level);


            for (var level = 1;
                 level <= 9;
                 level++)
            {
                maximumSlotsByLevel.TryGetValue(
                    level,
                    out var requestedMaximum);


                existingByLevel.TryGetValue(
                    level,
                    out var existing);


                if (requestedMaximum <= 0)
                {
                    if (existing is not null)
                    {
                        db.PlayerCharacterSpellSlots
                            .Remove(
                                existing);
                    }

                    continue;
                }


                if (existing is null)
                {
                    db.PlayerCharacterSpellSlots
                        .Add(
                            new PlayerCharacterSpellSlot
                            {
                                Id =
                                    Guid.NewGuid(),

                                PlayerCharacterSpellcastingId =
                                    spellcasting.Id,

                                Level =
                                    level,

                                MaximumSlots =
                                    requestedMaximum,

                                RemainingSlots =
                                    requestedMaximum
                            });

                    continue;
                }


                var oldMaximum =
                    existing.MaximumSlots;

                var difference =
                    requestedMaximum -
                    oldMaximum;


                existing.MaximumSlots =
                    requestedMaximum;


                if (difference > 0)
                {
                    existing.RemainingSlots =
                        Math.Min(
                            requestedMaximum,
                            existing.RemainingSlots +
                            difference);
                }
                else
                {
                    existing.RemainingSlots =
                        Math.Min(
                            existing.RemainingSlots,
                            requestedMaximum);
                }
            }


            character.UpdatedAtUtc =
                now;


            db.SaveChanges();
        }
    }

    public void AdjustSpellSlot(
        Guid characterId,
        int level,
        int amount)
    {
        if (level < 1 ||
            level > 9)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level));
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


            var spellcasting =
                db.PlayerCharacterSpellcastings
                    .SingleOrDefault(
                        x =>
                            x.PlayerCharacterId ==
                            characterId)
                ?? throw new InvalidOperationException(
                    "Spellcasting is not configured.");


            var slot =
                db.PlayerCharacterSpellSlots
                    .SingleOrDefault(
                        x =>
                            x.PlayerCharacterSpellcastingId ==
                                spellcasting.Id &&
                            x.Level ==
                                level)
                ?? throw new InvalidOperationException(
                    $"Level {level} spell slots are not configured.");


            slot.RemainingSlots =
                Math.Clamp(
                    slot.RemainingSlots + amount,
                    0,
                    slot.MaximumSlots);


            character.UpdatedAtUtc =
                DateTime.UtcNow;


            db.SaveChanges();
        }
    }

    public void RestoreAllSpellSlots(
        Guid characterId)
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


            var spellcasting =
                db.PlayerCharacterSpellcastings
                    .SingleOrDefault(
                        x =>
                            x.PlayerCharacterId ==
                            characterId)
                ?? throw new InvalidOperationException(
                    "Spellcasting is not configured.");


            var slots =
                db.PlayerCharacterSpellSlots
                    .Where(
                        x =>
                            x.PlayerCharacterSpellcastingId ==
                                spellcasting.Id)
                    .ToList();


            foreach (var slot in slots)
            {
                slot.RemainingSlots =
                    slot.MaximumSlots;
            }


            character.UpdatedAtUtc =
                DateTime.UtcNow;


            db.SaveChanges();
        }
    }

    public PlayerCharacterSpell SaveSpell(
        Guid characterId,
        Guid? spellId,
        PlayerCharacterSpell values)
    {
        if (string.IsNullOrWhiteSpace(
                values.Name))
        {
            throw new ArgumentException(
                "Spell name is required.");
        }

        if (values.Level < 0 ||
            values.Level > 9)
        {
            throw new ArgumentOutOfRangeException(
                nameof(values.Level),
                "Spell level must be between 0 and 9.");
        }

        if (values.SavingThrowAbility.HasValue &&
            !Enum.IsDefined(
                typeof(CharacterAbilityType),
                values.SavingThrowAbility.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(values.SavingThrowAbility));
        }

        var name =
            values.Name.Trim();

        if (name.Length > 150)
        {
            throw new ArgumentException(
                "Spell name cannot exceed 150 characters.");
        }

        static string? Normalize(
            string? value,
            int maximumLength,
            string fieldName)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return null;
            }

            var normalized =
                value.Trim();

            if (normalized.Length >
                maximumLength)
            {
                throw new ArgumentException(
                    $"{fieldName} cannot exceed {maximumLength} characters.");
            }

            return normalized;
        }

        var school =
            Normalize(
                values.School,
                50,
                "School");

        var castingTime =
            Normalize(
                values.CastingTime,
                100,
                "Casting time");

        var range =
            Normalize(
                values.Range,
                100,
                "Range");

        var area =
            Normalize(
                values.Area,
                100,
                "Area");

        var duration =
            Normalize(
                values.Duration,
                100,
                "Duration");

        var materialDescription =
            Normalize(
                values.MaterialDescription,
                500,
                "Material description");

        var notes =
            Normalize(
                values.Notes,
                2000,
                "Notes");

        var effects =
            values.EffectComponents
                .OrderBy(
                    x => x.SortOrder)
                .ToList();

        foreach (var effect
                 in effects)
        {
            if (!Enum.IsDefined(
                    typeof(SpellEffectType),
                    effect.EffectType))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(effect.EffectType));
            }

            if (string.IsNullOrWhiteSpace(
                    effect.Expression))
            {
                throw new ArgumentException(
                    "Spell effect expression is required.");
            }

            if (effect.Expression.Trim().Length >
                100)
            {
                throw new ArgumentException(
                    "Spell effect expression cannot exceed 100 characters.");
            }

            if (effect.EffectType ==
                    SpellEffectType.Damage &&
                string.IsNullOrWhiteSpace(
                    effect.DamageType))
            {
                throw new ArgumentException(
                    "Damage type is required for damage effects.");
            }

            if (effect.DamageType?.Trim().Length >
                100)
            {
                throw new ArgumentException(
                    "Damage type cannot exceed 100 characters.");
            }

            if (effect.UpcastExpressionPerLevel?
                    .Trim()
                    .Length >
                100)
            {
                throw new ArgumentException(
                    "Upcast expression cannot exceed 100 characters.");
            }

            if (effect.CantripScalingExpression?
                    .Trim()
                    .Length >
                100)
            {
                throw new ArgumentException(
                    "Cantrip scaling expression cannot exceed 100 characters.");
            }
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

            PlayerCharacterSpell spell;

            if (spellId.HasValue &&
                spellId.Value != Guid.Empty)
            {
                spell =
                    db.PlayerCharacterSpells
                        .SingleOrDefault(
                            x =>
                                x.Id ==
                                    spellId.Value &&
                                x.PlayerCharacterId ==
                                    characterId)
                    ?? throw new ArgumentException(
                        "Spell was not found.",
                        nameof(spellId));
            }
            else
            {
                var nextSortOrder =
                    db.PlayerCharacterSpells
                        .Where(
                            x =>
                                x.PlayerCharacterId ==
                                    characterId &&
                                x.Level ==
                                    values.Level)
                        .Select(
                            x =>
                                (int?)x.SortOrder)
                        .Max() ?? -1;

                spell =
                    new PlayerCharacterSpell
                    {
                        Id =
                            Guid.NewGuid(),

                        PlayerCharacterId =
                            characterId,

                        SortOrder =
                            nextSortOrder + 1,

                        CreatedAtUtc =
                            now
                    };

                db.PlayerCharacterSpells
                    .Add(
                        spell);
            }

            spell.Name =
                name;

            spell.Level =
                values.Level;

            spell.School =
                school;

            spell.CastingTime =
                castingTime;

            spell.Range =
                range;

            spell.Area =
                area;

            spell.Duration =
                duration;

            spell.HasVerbalComponent =
                values.HasVerbalComponent;

            spell.HasSomaticComponent =
                values.HasSomaticComponent;

            spell.HasMaterialComponent =
                values.HasMaterialComponent;

            spell.MaterialDescription =
                values.HasMaterialComponent
                    ? materialDescription
                    : null;

            spell.RequiresConcentration =
                values.RequiresConcentration;

            spell.IsRitual =
                values.IsRitual;

            spell.IsPrepared =
                values.IsPrepared;

            spell.HasSpellAttackRoll =
                values.HasSpellAttackRoll;

            spell.SavingThrowAbility =
                values.SavingThrowAbility;

            spell.Notes =
                notes;

            spell.UpdatedAtUtc =
                now;

            var existingEffects =
                db.PlayerCharacterSpellEffectComponents
                    .Where(
                        x =>
                            x.PlayerCharacterSpellId ==
                                spell.Id)
                    .ToList();

            if (existingEffects.Count > 0)
            {
                db.PlayerCharacterSpellEffectComponents
                    .RemoveRange(
                        existingEffects);
            }

            var newEffects =
                effects
                    .Select(
                        (effect, index) =>
                            new PlayerCharacterSpellEffectComponent
                            {
                                Id =
                                    Guid.NewGuid(),

                                PlayerCharacterSpellId =
                                    spell.Id,

                                EffectType =
                                    effect.EffectType,

                                Expression =
                                    effect.Expression.Trim(),

                                DamageType =
                                    effect.EffectType ==
                                        SpellEffectType.Damage
                                        ? effect.DamageType?.Trim()
                                        : null,

                                AddsSpellcastingAbilityModifier =
                                    effect.AddsSpellcastingAbilityModifier,

                                AdditionalBonus =
                                    effect.AdditionalBonus,

                                UpcastExpressionPerLevel =
                                    values.Level == 0
                                        ? null
                                        : string.IsNullOrWhiteSpace(
                                            effect.UpcastExpressionPerLevel)
                                            ? null
                                            : effect.UpcastExpressionPerLevel.Trim(),

                                CantripScalingExpression =
                                    values.Level != 0 ||
                                    string.IsNullOrWhiteSpace(
                                        effect.CantripScalingExpression)
                                        ? null
                                        : effect.CantripScalingExpression.Trim(),

                                SortOrder =
                                    index
                            })
                    .ToList();

            if (newEffects.Count > 0)
            {
                db.PlayerCharacterSpellEffectComponents
                    .AddRange(
                        newEffects);
            }

            if (!spell.RequiresConcentration)
            {
                var spellcasting =
                    db.PlayerCharacterSpellcastings
                        .SingleOrDefault(
                            x =>
                                x.PlayerCharacterId ==
                                characterId);

                if (spellcasting?.ActiveConcentrationSpellId ==
                    spell.Id)
                {
                    spellcasting.ActiveConcentrationSpellId =
                        null;

                    spellcasting.UpdatedAtUtc =
                        now;
                }
            }

            character.UpdatedAtUtc =
                now;

            db.SaveChanges();

            return spell;
        }
    }

    public void DeleteSpell(
    Guid characterId,
    Guid spellId)
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

            var spell =
                db.PlayerCharacterSpells
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                                spellId &&
                            x.PlayerCharacterId ==
                                characterId);

            if (spell is null)
            {
                return;
            }

            var spellcasting =
                db.PlayerCharacterSpellcastings
                    .SingleOrDefault(
                        x =>
                            x.PlayerCharacterId ==
                            characterId);

            if (spellcasting?.ActiveConcentrationSpellId ==
                spell.Id)
            {
                spellcasting.ActiveConcentrationSpellId =
                    null;

                spellcasting.UpdatedAtUtc =
                    DateTime.UtcNow;
            }

            db.PlayerCharacterSpells
                .Remove(
                    spell);

            character.UpdatedAtUtc =
                DateTime.UtcNow;

            db.SaveChanges();
        }
    }

    public void CastSpell(
        Guid characterId,
        Guid spellId,
        int? slotLevel,
        bool castAsRitual = false)
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

            var spell =
                db.PlayerCharacterSpells
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                                spellId &&
                            x.PlayerCharacterId ==
                                characterId)
                ?? throw new ArgumentException(
                    "Spell was not found.",
                    nameof(spellId));

            var spellcasting =
                db.PlayerCharacterSpellcastings
                    .Include(
                        x => x.SpellSlots)
                    .SingleOrDefault(
                        x =>
                            x.PlayerCharacterId ==
                            characterId)
                ?? throw new InvalidOperationException(
                    "Spellcasting is not configured.");

            if (castAsRitual)
            {
                if (!spell.IsRitual)
                {
                    throw new InvalidOperationException(
                        "This spell cannot be cast as a ritual.");
                }
            }
            else if (spell.Level > 0)
            {
                if (!slotLevel.HasValue)
                {
                    throw new InvalidOperationException(
                        "Choose a spell slot level.");
                }

                if (slotLevel.Value <
                        spell.Level ||
                    slotLevel.Value > 9)
                {
                    throw new InvalidOperationException(
                        "The selected spell slot cannot cast this spell.");
                }

                var slot =
                    spellcasting.SpellSlots
                        .SingleOrDefault(
                            x =>
                                x.Level ==
                                slotLevel.Value)
                    ?? throw new InvalidOperationException(
                        $"Level {slotLevel.Value} spell slots are not configured.");

                if (slot.RemainingSlots <= 0)
                {
                    throw new InvalidOperationException(
                        $"No level {slotLevel.Value} spell slots remain.");
                }

                slot.RemainingSlots--;
            }
            else if (slotLevel.HasValue)
            {
                throw new InvalidOperationException(
                    "Cantrips do not use spell slots.");
            }

            var now =
                DateTime.UtcNow;

            if (spell.RequiresConcentration)
            {
                spellcasting.ActiveConcentrationSpellId =
                    spell.Id;

                spellcasting.UpdatedAtUtc =
                    now;
            }

            character.UpdatedAtUtc =
                now;

            db.SaveChanges();
        }
    }

    public void EndConcentration(
        Guid characterId)
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

            var spellcasting =
                db.PlayerCharacterSpellcastings
                    .SingleOrDefault(
                        x =>
                            x.PlayerCharacterId ==
                            characterId);

            if (spellcasting is null ||
                !spellcasting.ActiveConcentrationSpellId.HasValue)
            {
                return;
            }

            var now =
                DateTime.UtcNow;

            spellcasting.ActiveConcentrationSpellId =
                null;

            spellcasting.UpdatedAtUtc =
                now;

            character.UpdatedAtUtc =
                now;

            db.SaveChanges();
        }
    }

    public void ToggleSpellPrepared(
        Guid characterId,
        Guid spellId)
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

            var spell =
                db.PlayerCharacterSpells
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                                spellId &&
                            x.PlayerCharacterId ==
                                characterId)
                ?? throw new ArgumentException(
                    "Spell was not found.",
                    nameof(spellId));

            spell.IsPrepared =
                !spell.IsPrepared;

            spell.UpdatedAtUtc =
                DateTime.UtcNow;

            character.UpdatedAtUtc =
                DateTime.UtcNow;

            db.SaveChanges();
        }
    }
}