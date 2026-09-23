using InitiativeTracker.Web.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace InitiativeTracker.Web.Services;

public class PlayerRegistryService
{
    private readonly object _lock =
        new();

    private readonly IDbContextFactory<InitiativeTrackerDbContext>
        _dbContextFactory;

    public PlayerRegistryService(
        IDbContextFactory<InitiativeTrackerDbContext> dbContextFactory)
    {
        _dbContextFactory =
            dbContextFactory;
    }

    public PlayerConnection JoinPlayer(
        Guid sessionId,
        string name,
        string? existingProfileToken,
        out PlayerProfile profile)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Player name is required.",
                nameof(name));
        }

        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var now =
                DateTime.UtcNow;

            PlayerProfile? existingProfile =
                null;

            if (!string.IsNullOrWhiteSpace(
                    existingProfileToken))
            {
                existingProfile =
                    db.PlayerProfiles
                        .SingleOrDefault(
                            x =>
                                x.AccessToken ==
                                existingProfileToken);
            }

            if (existingProfile is null)
            {
                existingProfile =
                    new PlayerProfile
                    {
                        Name =
                            name.Trim(),

                        AccessToken =
                            GenerateProfileToken(),

                        CreatedAtUtc =
                            now,

                        UpdatedAtUtc =
                            now
                    };

                db.PlayerProfiles.Add(
                    existingProfile);
            }
            else
            {
                existingProfile.Name =
                    name.Trim();

                existingProfile.UpdatedAtUtc =
                    now;
            }

            var connection =
                new PlayerConnection
                {
                    SessionId =
                            sessionId,

                    PlayerProfileId =
                            existingProfile.Id,

                    Name =
                            existingProfile.Name,

                    IsActive =
                            true,

                    JoinedAtUtc =
                            now,

                    LastSeenAtUtc =
                            now
                };

            db.PlayerConnections.Add(
                connection);

            db.SaveChanges();

            profile =
                existingProfile;

            return connection;
        }
    }

    private static string GenerateProfileToken()
    {
        var bytes =
            RandomNumberGenerator
                .GetBytes(32);

        return WebEncoders
            .Base64UrlEncode(bytes);
    }

    public IReadOnlyList<PlayerProfile> GetAllProfiles()
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        return db.PlayerProfiles
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToList();
    }

    public void PermanentlyDeleteProfile(
        Guid playerProfileId)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var profile =
                db.PlayerProfiles
                    .SingleOrDefault(
                        x => x.Id == playerProfileId);

            if (profile is null)
            {
                return;
            }

            var connections =
                db.PlayerConnections
                    .Where(
                        x =>
                            x.PlayerProfileId ==
                            playerProfileId)
                    .ToList();

            var characters =
                db.PlayerCharacters
                    .Where(
                        x =>
                            x.PlayerProfileId ==
                            playerProfileId)
                    .ToList();

            db.PlayerConnections
                .RemoveRange(connections);

            db.PlayerCharacters
                .RemoveRange(characters);

            db.PlayerProfiles.Remove(
                profile);

            db.SaveChanges();
        }
    }

    public IReadOnlyList<PlayerConnection> GetActivePlayers(
            Guid sessionId)
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        return db.PlayerConnections
            .AsNoTracking()
            .Where(x =>
                x.SessionId == sessionId &&
                x.IsActive)
            .OrderBy(x => x.Name)
            .ToList();
    }

    public PlayerConnection? GetPlayer(
            Guid playerId)
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        return db.PlayerConnections
            .AsNoTracking()
            .SingleOrDefault(
                x => x.Id == playerId);
    }

    public bool IsActivePlayer(
        Guid playerId,
        Guid sessionId)
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        return db.PlayerConnections
            .AsNoTracking()
            .Any(x =>
                x.Id == playerId &&
                x.SessionId == sessionId &&
                x.IsActive);
    }

    public void RemovePlayer(
        Guid playerId)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var player =
                db.PlayerConnections
                    .SingleOrDefault(
                        x => x.Id == playerId);

            if (player is null)
            {
                return;
            }

            player.IsActive =
                false;

            db.SaveChanges();
        }
    }

    public void UpdateLastSeen(
        Guid playerId)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var player =
                db.PlayerConnections
                    .SingleOrDefault(
                        x => x.Id == playerId);

            if (player is null)
            {
                return;
            }

            player.LastSeenAtUtc =
                DateTime.UtcNow;

            db.SaveChanges();
        }
    }

    public PlayerProfile? FindProfileByToken(
        string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        using var db =
            _dbContextFactory.CreateDbContext();

        return db.PlayerProfiles
            .AsNoTracking()
            .SingleOrDefault(
                x => x.AccessToken == token);
    }
}