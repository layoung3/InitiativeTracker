using InitiativeTracker.Web.Data;
using Microsoft.EntityFrameworkCore;

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
        string name)
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

            var player =
                new PlayerConnection
                {
                    SessionId =
                        sessionId,

                    Name =
                        name.Trim(),

                    IsActive =
                        true,

                    JoinedAtUtc =
                        now,

                    LastSeenAtUtc =
                        now
                };

            db.PlayerConnections.Add(
                player);

            db.SaveChanges();

            return player;
        }
    }

    public IReadOnlyList<PlayerConnection>
        GetActivePlayers(
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

    public PlayerConnection?
        GetPlayer(
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
}