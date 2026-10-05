using InitiativeTracker.Web.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace InitiativeTracker.Web.Services;

public class PlayerRegistryService
{
    private static readonly TimeSpan PresenceWindow =
        TimeSpan.FromMinutes(2);

    private static readonly TimeSpan LastSeenWriteInterval =
        TimeSpan.FromSeconds(20);

    private static readonly TimeSpan ConnectionHistoryRetention =
        TimeSpan.FromDays(30);

    private const string RecoveryAlphabet =
        "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

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


    public PlayerProfile? FindRememberedProfile(
        string? deviceToken,
        string? legacyProfileToken)
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        if (!string.IsNullOrWhiteSpace(
                deviceToken))
        {
            var tokenHash =
                HashSecret(deviceToken);

            var credential =
                db.PlayerDeviceCredentials
                    .AsNoTracking()
                    .SingleOrDefault(
                        x =>
                            x.TokenHash == tokenHash &&
                            x.RevokedAtUtc == null);

            if (credential is not null)
            {
                return db.PlayerProfiles
                    .AsNoTracking()
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                            credential.PlayerProfileId);
            }
        }

        /*
         * Compatibility path for profiles created before
         * per-device credentials were introduced.
         */
        if (!string.IsNullOrWhiteSpace(
                legacyProfileToken))
        {
            return db.PlayerProfiles
                .AsNoTracking()
                .SingleOrDefault(
                    x =>
                        x.AccessToken ==
                        legacyProfileToken);
        }

        return null;
    }


    public PlayerJoinResult JoinRememberedPlayer(
        Guid sessionId,
        string? deviceToken,
        string? legacyProfileToken)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var now =
                DateTime.UtcNow;

            PlayerProfile? profile =
                null;

            PlayerDeviceCredential? credential =
                null;

            string? issuedDeviceToken =
                null;

            string? issuedRecoveryKey =
                null;


            if (!string.IsNullOrWhiteSpace(
                    deviceToken))
            {
                var tokenHash =
                    HashSecret(deviceToken);

                credential =
                    db.PlayerDeviceCredentials
                        .SingleOrDefault(
                            x =>
                                x.TokenHash ==
                                tokenHash &&
                                x.RevokedAtUtc ==
                                null);

                if (credential is not null)
                {
                    profile =
                        db.PlayerProfiles
                            .SingleOrDefault(
                                x =>
                                    x.Id ==
                                    credential.PlayerProfileId);
                }
            }


            if (profile is null &&
                !string.IsNullOrWhiteSpace(
                    legacyProfileToken))
            {
                profile =
                    db.PlayerProfiles
                        .SingleOrDefault(
                            x =>
                                x.AccessToken ==
                                legacyProfileToken);

                if (profile is not null)
                {
                    issuedDeviceToken =
                        GenerateDeviceToken();

                    credential =
                        CreateDeviceCredential(
                            profile.Id,
                            issuedDeviceToken,
                            now);

                    db.PlayerDeviceCredentials.Add(
                        credential);
                }
            }


            if (profile is null ||
                credential is null)
            {
                throw new InvalidOperationException(
                    "This browser is not linked to a saved player profile.");
            }


            if (string.IsNullOrWhiteSpace(
                    profile.RecoveryKeyHash))
            {
                issuedRecoveryKey =
                    GenerateRecoveryKey();

                profile.RecoveryKeyHash =
                    HashRecoveryKey(
                        issuedRecoveryKey);
            }


            return CompleteJoin(
                db,
                sessionId,
                profile,
                credential,
                now,
                issuedDeviceToken,
                issuedRecoveryKey);
        }
    }


    public PlayerJoinResult CreatePlayer(
        Guid sessionId,
        string name)
    {
        if (string.IsNullOrWhiteSpace(
                name))
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

            var recoveryKey =
                GenerateRecoveryKey();

            var deviceToken =
                GenerateDeviceToken();

            var profile =
                new PlayerProfile
                {
                    Name =
                        name.Trim(),

                    AccessToken =
                        null,

                    RecoveryKeyHash =
                        HashRecoveryKey(
                            recoveryKey),

                    CreatedAtUtc =
                        now,

                    UpdatedAtUtc =
                        now,

                    LastSeenAtUtc =
                        now
                };

            db.PlayerProfiles.Add(
                profile);


            var credential =
                CreateDeviceCredential(
                    profile.Id,
                    deviceToken,
                    now);

            db.PlayerDeviceCredentials.Add(
                credential);


            return CompleteJoin(
                db,
                sessionId,
                profile,
                credential,
                now,
                deviceToken,
                recoveryKey);
        }
    }


    public PlayerJoinResult RecoverPlayer(
        Guid sessionId,
        string recoveryKey)
    {
        if (string.IsNullOrWhiteSpace(
                recoveryKey))
        {
            throw new ArgumentException(
                "Recovery key is required.",
                nameof(recoveryKey));
        }

        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var recoveryHash =
                HashRecoveryKey(
                    recoveryKey);

            var profile =
                db.PlayerProfiles
                    .SingleOrDefault(
                        x =>
                            x.RecoveryKeyHash ==
                            recoveryHash)
                ?? throw new InvalidOperationException(
                    "Recovery key was not recognized.");

            var now =
                DateTime.UtcNow;

            var deviceToken =
                GenerateDeviceToken();

            var credential =
                CreateDeviceCredential(
                    profile.Id,
                    deviceToken,
                    now);

            db.PlayerDeviceCredentials.Add(
                credential);


            return CompleteJoin(
                db,
                sessionId,
                profile,
                credential,
                now,
                deviceToken,
                null);
        }
    }


    public IReadOnlyList<PlayerConnection> GetActivePlayers(
        Guid sessionId)
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        var cutoff =
            DateTime.UtcNow -
            PresenceWindow;

        return db.PlayerConnections
            .AsNoTracking()
            .Where(
                x =>
                    x.SessionId ==
                    sessionId &&
                    x.IsActive &&
                    x.LastSeenAtUtc.HasValue &&
                    x.LastSeenAtUtc.Value >=
                    cutoff)
            .OrderBy(
                x => x.Name)
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
            .Any(
                x =>
                    x.Id ==
                    playerId &&
                    x.SessionId ==
                    sessionId &&
                    x.IsActive);
    }


    public bool IsActivePlayerCredential(
        Guid credentialId,
        Guid playerProfileId)
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        return db.PlayerDeviceCredentials
            .AsNoTracking()
            .Any(
                x =>
                    x.Id ==
                    credentialId &&
                    x.PlayerProfileId ==
                    playerProfileId &&
                    x.RevokedAtUtc ==
                    null);
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
                        x =>
                            x.Id ==
                            playerId);

            if (player is null ||
                !player.IsActive)
            {
                return;
            }

            var now =
                DateTime.UtcNow;

            if (player.LastSeenAtUtc.HasValue &&
                now - player.LastSeenAtUtc.Value <
                LastSeenWriteInterval)
            {
                return;
            }


            player.LastSeenAtUtc =
                now;


            var profile =
                db.PlayerProfiles
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                            player.PlayerProfileId);

            if (profile is not null)
            {
                profile.LastSeenAtUtc =
                    now;

                profile.UpdatedAtUtc =
                    now;
            }


            if (player.PlayerDeviceCredentialId
                .HasValue)
            {
                var credential =
                    db.PlayerDeviceCredentials
                        .SingleOrDefault(
                            x =>
                                x.Id ==
                                player
                                    .PlayerDeviceCredentialId
                                    .Value);

                if (credential is not null &&
                    credential.RevokedAtUtc is null)
                {
                    credential.LastSeenAtUtc =
                        now;
                }
            }


            db.SaveChanges();
        }
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
                        x =>
                            x.Id ==
                            playerId);

            if (player is null)
            {
                return;
            }

            player.IsActive =
                false;

            db.SaveChanges();
        }
    }


    public IReadOnlyList<PlayerProfile>
        GetAllProfiles()
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        return db.PlayerProfiles
            .AsNoTracking()
            .OrderBy(
                x => x.Name)
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
                        x =>
                            x.Id ==
                            playerProfileId);

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

            var credentials =
                db.PlayerDeviceCredentials
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
                .RemoveRange(
                    connections);

            db.PlayerDeviceCredentials
                .RemoveRange(
                    credentials);

            db.PlayerCharacters
                .RemoveRange(
                    characters);

            db.PlayerProfiles.Remove(
                profile);

            db.SaveChanges();
        }
    }


    public IReadOnlyList<PlayerDeviceCredential>
        GetPlayerDevices(
            Guid playerProfileId)
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        return db.PlayerDeviceCredentials
            .AsNoTracking()
            .Where(
                x =>
                    x.PlayerProfileId ==
                    playerProfileId &&
                    x.RevokedAtUtc ==
                    null)
            .OrderByDescending(
                x => x.LastSeenAtUtc)
            .ToList();
    }


    public string RegenerateRecoveryKey(
        Guid playerProfileId)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var profile =
                db.PlayerProfiles
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                            playerProfileId)
                ?? throw new ArgumentException(
                    "Player profile was not found.",
                    nameof(playerProfileId));

            var recoveryKey =
                GenerateRecoveryKey();

            profile.RecoveryKeyHash =
                HashRecoveryKey(
                    recoveryKey);

            profile.UpdatedAtUtc =
                DateTime.UtcNow;

            db.SaveChanges();

            return recoveryKey;
        }
    }


    public void RenameDevice(
        Guid playerProfileId,
        Guid credentialId,
        string name)
    {
        if (string.IsNullOrWhiteSpace(
                name))
        {
            throw new ArgumentException(
                "Device name is required.",
                nameof(name));
        }

        var normalizedName =
            name.Trim();

        if (normalizedName.Length > 100)
        {
            throw new ArgumentException(
                "Device name must be 100 characters or fewer.",
                nameof(name));
        }

        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var credential =
                db.PlayerDeviceCredentials
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                            credentialId &&
                            x.PlayerProfileId ==
                            playerProfileId &&
                            x.RevokedAtUtc ==
                            null)
                ?? throw new ArgumentException(
                    "Linked device was not found.",
                    nameof(credentialId));

            credential.Name =
                normalizedName;

            db.SaveChanges();
        }
    }


    public IReadOnlyList<Guid> RevokeDevice(
        Guid playerProfileId,
        Guid credentialId)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var credential =
                db.PlayerDeviceCredentials
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                            credentialId &&
                            x.PlayerProfileId ==
                            playerProfileId)
                ?? throw new ArgumentException(
                    "Linked device was not found.",
                    nameof(credentialId));

            if (credential.RevokedAtUtc is not null)
            {
                return [];
            }

            credential.RevokedAtUtc =
                DateTime.UtcNow;

            var connections =
                db.PlayerConnections
                    .Where(
                        x =>
                            x.PlayerProfileId ==
                            playerProfileId &&
                            x.PlayerDeviceCredentialId ==
                            credentialId &&
                            x.IsActive)
                    .ToList();

            var removedConnectionIds =
                connections
                    .Select(
                        x => x.Id)
                    .ToList();

            foreach (var connection
                     in connections)
            {
                connection.IsActive =
                    false;
            }

            db.SaveChanges();

            return removedConnectionIds;
        }
    }


    public IReadOnlyList<Guid> RevokeAllDevices(
        Guid playerProfileId)
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory.CreateDbContext();

            var now =
                DateTime.UtcNow;

            var credentials =
                db.PlayerDeviceCredentials
                    .Where(
                        x =>
                            x.PlayerProfileId ==
                            playerProfileId &&
                            x.RevokedAtUtc ==
                            null)
                    .ToList();

            foreach (var credential
                     in credentials)
            {
                credential.RevokedAtUtc =
                    now;
            }

            /*
             * Mark every active presence for this profile inactive,
             * including any legacy connection that predates
             * PlayerDeviceCredentialId.
             */
            var connections =
                db.PlayerConnections
                    .Where(
                        x =>
                            x.PlayerProfileId ==
                            playerProfileId &&
                            x.IsActive)
                    .ToList();

            var removedConnectionIds =
                connections
                    .Select(
                        x => x.Id)
                    .ToList();

            foreach (var connection
                     in connections)
            {
                connection.IsActive =
                    false;
            }

            db.SaveChanges();

            return removedConnectionIds;
        }
    }


    public IReadOnlyList<PlayerProfileAdminView>
        GetProfileAdminViews()
    {
        using var db =
            _dbContextFactory.CreateDbContext();

        return db.PlayerProfiles
            .AsNoTracking()
            .OrderBy(
                x => x.Name)
            .Select(
                profile =>
                    new PlayerProfileAdminView
                    {
                        Id =
                            profile.Id,

                        Name =
                            profile.Name,

                        CreatedAtUtc =
                            profile.CreatedAtUtc,

                        LastSeenAtUtc =
                            profile.LastSeenAtUtc,

                        CharacterCount =
                            db.PlayerCharacters
                                .Count(
                                    character =>
                                        character.PlayerProfileId ==
                                        profile.Id &&
                                        character.IsActive),

                        LinkedDeviceCount =
                            db.PlayerDeviceCredentials
                                .Count(
                                    device =>
                                        device.PlayerProfileId ==
                                        profile.Id &&
                                        device.RevokedAtUtc ==
                                        null)
                    })
            .ToList();
    }


    private static PlayerDeviceCredential
        CreateDeviceCredential(
            Guid playerProfileId,
            string rawToken,
            DateTime now)
    {
        return new PlayerDeviceCredential
        {
            PlayerProfileId =
                playerProfileId,

            TokenHash =
                HashSecret(
                    rawToken),

            Name =
                "Browser",

            CreatedAtUtc =
                now,

            LastSeenAtUtc =
                now
        };
    }


    private static PlayerJoinResult CompleteJoin(
        InitiativeTrackerDbContext db,
        Guid sessionId,
        PlayerProfile profile,
        PlayerDeviceCredential credential,
        DateTime now,
        string? issuedDeviceToken,
        string? issuedRecoveryKey)
    {
        profile.LastSeenAtUtc =
            now;

        profile.UpdatedAtUtc =
            now;

        credential.LastSeenAtUtc =
            now;


        /*
         * Presence rows are not player data. Keep only a small
         * recent history so a long-lived store/home hub does not
         * accumulate years of obsolete connection records.
         */
        var connectionHistoryCutoff =
            now -
            ConnectionHistoryRetention;

        var expiredConnectionHistory =
            db.PlayerConnections
                .Where(
                    x =>
                        x.LastSeenAtUtc.HasValue &&
                        x.LastSeenAtUtc.Value <
                        connectionHistoryCutoff)
                .ToList();

        db.PlayerConnections.RemoveRange(
            expiredConnectionHistory);


        /*
         * A browser/device should have at most one active
         * presence row for a given game session.
         */
        var existingConnections =
            db.PlayerConnections
                .Where(
                    x =>
                        x.SessionId ==
                        sessionId &&
                        x.PlayerDeviceCredentialId ==
                        credential.Id &&
                        x.IsActive)
                .ToList();

        foreach (var existing
                 in existingConnections)
        {
            existing.IsActive =
                false;
        }


        var connection =
            new PlayerConnection
            {
                SessionId =
                    sessionId,

                PlayerProfileId =
                    profile.Id,

                PlayerDeviceCredentialId =
                    credential.Id,

                Name =
                    profile.Name,

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


        return new PlayerJoinResult
        {
            Connection =
                connection,

            Profile =
                profile,

            DeviceCredential =
                credential,

            DeviceTokenToStore =
                issuedDeviceToken,

            RecoveryKeyToShow =
                issuedRecoveryKey
        };
    }


    private static string GenerateDeviceToken()
    {
        var bytes =
            RandomNumberGenerator
                .GetBytes(32);

        return WebEncoders
            .Base64UrlEncode(
                bytes);
    }


    private static string GenerateRecoveryKey()
    {
        const int groupCount = 5;
        const int groupLength = 5;

        var groups =
            new string[groupCount];

        for (var groupIndex = 0;
             groupIndex < groupCount;
             groupIndex++)
        {
            Span<char> chars =
                stackalloc char[groupLength];

            for (var charIndex = 0;
                 charIndex < groupLength;
                 charIndex++)
            {
                var alphabetIndex =
                    RandomNumberGenerator
                        .GetInt32(
                            RecoveryAlphabet.Length);

                chars[charIndex] =
                    RecoveryAlphabet[
                        alphabetIndex];
            }

            groups[groupIndex] =
                new string(chars);
        }

        return string.Join(
            '-',
            groups);
    }


    private static string HashRecoveryKey(
        string recoveryKey)
    {
        var normalized =
            NormalizeRecoveryKey(
                recoveryKey);

        return HashSecret(
            normalized);
    }


    private static string NormalizeRecoveryKey(
        string recoveryKey)
    {
        return new string(
            recoveryKey
                .Where(
                    char.IsLetterOrDigit)
                .Select(
                    char.ToUpperInvariant)
                .ToArray());
    }


    private static string HashSecret(
        string secret)
    {
        var bytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    secret));

        return Convert.ToHexString(
            bytes);
    }
}


public sealed class PlayerJoinResult
{
    public required PlayerConnection Connection { get; init; }

    public required PlayerProfile Profile { get; init; }

    public required PlayerDeviceCredential DeviceCredential { get; init; }

    public string? DeviceTokenToStore { get; init; }

    public string? RecoveryKeyToShow { get; init; }
}


public sealed class PlayerProfileAdminView
{
    public Guid Id { get; init; }

    public string Name { get; init; } =
        string.Empty;

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? LastSeenAtUtc { get; init; }

    public int CharacterCount { get; init; }

    public int LinkedDeviceCount { get; init; }
}
