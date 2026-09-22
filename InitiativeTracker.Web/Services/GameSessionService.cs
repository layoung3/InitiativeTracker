using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using InitiativeTracker.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace InitiativeTracker.Web.Services;

public class GameSessionService
{
    private const string JoinAlphabet =
        "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly object _lock = new();

    private readonly IDbContextFactory<InitiativeTrackerDbContext>
        _dbContextFactory;

    public GameSessionRecord CurrentSession { get; private set; }

    public GameSessionService(
        IDbContextFactory<InitiativeTrackerDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;

        CurrentSession =
            LoadOrCreateSession();
    }

    public bool ValidateDmPin(
        string? pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            return false;
        }

        lock (_lock)
        {
            return string.Equals(
                CurrentSession.DmPin,
                pin.Trim(),
                StringComparison.Ordinal);
        }
    }

    public bool ValidateJoinCode(
        string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        lock (_lock)
        {
            return string.Equals(
                CurrentSession.JoinCode,
                code.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool ValidatePlayerToken(
        string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        lock (_lock)
        {
            return string.Equals(
                CurrentSession.PlayerJoinToken,
                token,
                StringComparison.Ordinal);
        }
    }

    public bool ValidateDisplayToken(
        string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        lock (_lock)
        {
            return string.Equals(
                CurrentSession.DisplayToken,
                token,
                StringComparison.Ordinal);
        }
    }

    private GameSessionRecord LoadOrCreateSession()
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        var existing =
            db.GameSessions
                .AsNoTracking()
                .SingleOrDefault(x => x.Id == 1);

        if (existing is not null)
        {
            return existing;
        }

        var session =
            new GameSessionRecord
            {
                Id = 1,

                SessionId =
                    Guid.NewGuid(),

                JoinCode =
                    GenerateJoinCode(),

                DmPin =
                    GenerateDmPin(),

                PlayerJoinToken =
                    GenerateToken(),

                DisplayToken =
                    GenerateToken(),

                CreatedAtUtc =
                    DateTime.UtcNow
            };

        db.GameSessions.Add(session);

        db.SaveChanges();

        return session;
    }

    private static string GenerateJoinCode(
        int length = 6)
    {
        Span<char> result =
            stackalloc char[length];

        for (var i = 0; i < length; i++)
        {
            var index =
                RandomNumberGenerator.GetInt32(
                    JoinAlphabet.Length);

            result[i] =
                JoinAlphabet[index];
        }

        return new string(result);
    }

    private static string GenerateDmPin()
    {
        var pin =
            RandomNumberGenerator.GetInt32(
                0,
                1_000_000);

        return pin.ToString("D6");
    }

    private static string GenerateToken()
    {
        var bytes =
            RandomNumberGenerator.GetBytes(32);

        return WebEncoders.Base64UrlEncode(
            bytes);
    }
}