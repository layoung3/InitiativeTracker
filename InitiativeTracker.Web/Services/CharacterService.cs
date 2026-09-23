using InitiativeTracker.Web.Data;
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

            existing.MaxHp =
                Math.Max(0, character.MaxHp);

            existing.CurrentHp =
                Math.Clamp(
                    character.CurrentHp,
                    0,
                    existing.MaxHp);

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
}