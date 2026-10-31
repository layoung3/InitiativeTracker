using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace InitiativeTracker.Web.Infrastructure;

public sealed class SecureAccessDenied : Exception { }
public sealed class SecureAccessUnavailable : Exception { }
public sealed record SecureIdentity(string Role, Guid Session, Guid? Profile, Guid? Player, Guid? Credential, Guid? Display, string Stamp);
public sealed record SecureGrant(Guid Id, SecureIdentity Identity, string Browser, string Origin, Guid Epoch, int Generation,
    DateTimeOffset Expires, bool Revoked = false);
public sealed class SecureGrantState
{
    public int Format { get; set; } = 1;
    public Guid Epoch { get; set; } = Guid.NewGuid();
    public List<SecureGrant> Grants { get; set; } = [];
}
public sealed record SecureEnrollment(Guid Id, string Reference, string Purpose, SecureIdentity Identity, string Browser,
    string Origin, DateTimeOffset Expires, string Code, int Attempts = 0, Guid? Approver = null, int ApproverGeneration = 0);
public sealed record SecureAccessStorage(string Root, bool Persistent);

/// <summary>Protected durable state with a write-ahead fence. No automatic legacy enrollment or backup recovery.</summary>
public class SecureGrantPersistence(SecureAccessStorage storage, IDataProtectionProvider protection)
{
    private readonly IDataProtector protector = protection.CreateProtector("InitiativeTracker.SecureAccess.State.v1");
    public virtual SecureGrantState? Read()
    {
        if (!storage.Persistent) return null;
        var path = Path.Combine(storage.Root, "state.bin");
        if (File.Exists(Path.Combine(storage.Root, "mutation.pending")) || !File.Exists(path)) return null;
        try
        {
            BrowserCertificateStore.CheckPath(storage.Root, true);
            BrowserCertificateStore.CheckPath(path, false);
            if (new FileInfo(path).Length > 1_048_576) return null;
            var plain = protector.Unprotect(File.ReadAllBytes(path));
            using var document = JsonDocument.Parse(plain);
            if (!document.RootElement.TryGetProperty("Format", out _) || !document.RootElement.TryGetProperty("Epoch", out _) ||
                !document.RootElement.TryGetProperty("Grants", out _)) return null;
            var state = JsonSerializer.Deserialize<SecureGrantState>(plain);
            return state is { Format: 1 } && state.Epoch != Guid.Empty && state.Grants is { Count: <= 256 } &&
                state.Grants.All(g => g is not null && g.Identity is not null && g.Id != Guid.Empty && g.Epoch == state.Epoch && g.Generation >= 1 &&
                    g.Browser is { Length: 64 } && g.Identity.Stamp is { Length: 64 } && g.Identity.Role is "DM" or "Player" or "Display") &&
                state.Grants.Select(g => g.Id).Distinct().Count() == state.Grants.Count ? state : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidOperationException)
        { return null; }
    }
    public virtual void Write(SecureGrantState state, bool recovery = false)
    {
        if (!storage.Persistent || !OperatingSystem.IsLinux()) throw new SecureAccessUnavailable();
        try
        {
            Directory.CreateDirectory(storage.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            BrowserCertificateStore.CheckPath(storage.Root, true);
            var fence = Path.Combine(storage.Root, "mutation.pending");
            if (File.Exists(fence) && !recovery) throw new SecureAccessUnavailable();
            if (File.Exists(fence)) BrowserCertificateStore.CheckPath(fence, false);
            using (var marker = PrivateFile(fence, FileMode.Create)) { marker.WriteByte(1); marker.Flush(true); }
            var bytes = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(state));
            if (bytes.Length > 1_048_576) throw new SecureAccessUnavailable();
            var temporary = Path.Combine(storage.Root, ".state-" + Guid.NewGuid().ToString("N"));
            using (var output = PrivateFile(temporary, FileMode.CreateNew)) { output.Write(bytes); output.Flush(true); }
            File.Move(temporary, Path.Combine(storage.Root, "state.bin"), true);
            File.Delete(fence);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException)
        { throw new SecureAccessUnavailable(); }
    }
    private static FileStream PrivateFile(string path, FileMode mode)
    {
        if (!OperatingSystem.IsLinux()) throw new SecureAccessUnavailable();
        return new(path, new FileStreamOptions
    {
        Mode = mode, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.WriteThrough,
        UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
    });
    }
}

public sealed class SecureAccessService(SecureGrantPersistence persistence, IDbContextFactory<InitiativeTrackerDbContext> factory,
    GameSessionService session, TimeProvider clock)
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, SecureEnrollment> pending = [];
    private readonly Dictionary<string, (DateTimeOffset Start, int Count)> rates = [];
    private bool failed;
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private DateTimeOffset Now => clock.GetUtcNow();
    public bool Initialized { get { lock (gate) return !failed && persistence.Read() is not null; } }

    public SecureIdentity? Capture(ClaimsPrincipal user, bool physicalDm = false)
    {
        using var db = factory.CreateDbContext();
        var current = db.GameSessions.AsNoTracking().SingleOrDefault(x => x.Id == 1);
        if (current is null || current.SessionId != session.CurrentSession.SessionId) return null;
        var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        var role = physicalDm ? "DM" : roles.Length == 1 ? roles[0] : "";
        if (!physicalDm && (user.Identity?.IsAuthenticated != true ||
            user.FindFirstValue("session_id") != current.SessionId.ToString())) return null;
        if (role == "DM") return new(role, current.SessionId, null, null, null, null, Hash(current.DmPin));
        if (role == "Player" && Guid.TryParse(user.FindFirstValue("player_profile_id"), out var profileId) &&
            Guid.TryParse(user.FindFirstValue("player_id"), out var playerId) && Guid.TryParse(user.FindFirstValue("player_device_id"), out var credentialId))
        {
            var profile = db.PlayerProfiles.AsNoTracking().SingleOrDefault(x => x.Id == profileId);
            var player = db.PlayerConnections.AsNoTracking().SingleOrDefault(x => x.Id == playerId);
            var credential = db.PlayerDeviceCredentials.AsNoTracking().SingleOrDefault(x => x.Id == credentialId);
            if (profile is null || player is null || credential is null || !player.IsActive || player.SessionId != current.SessionId ||
                player.PlayerProfileId != profileId || player.PlayerDeviceCredentialId != credentialId ||
                credential.PlayerProfileId != profileId || credential.RevokedAtUtc is not null) return null;
            return new(role, current.SessionId, profileId, playerId, credentialId, null,
                Hash($"{profile.CreatedAtUtc:O}|{profile.RecoveryKeyHash}|{credential.TokenHash}|{credential.CreatedAtUtc:O}"));
        }
        if (role == "Display" && Guid.TryParse(user.FindFirstValue("device_id"), out var deviceId))
        {
            var device = db.RegisteredDevices.AsNoTracking().SingleOrDefault(x => x.Id == deviceId);
            if (device is { IsTrusted: true, DeviceType: "Display" })
                return new(role, current.SessionId, null, null, null, deviceId, Hash($"{device.PairedAtUtc:O}|{device.DeviceType}"));
        }
        return null;
    }

    public static ClaimsPrincipal Principal(SecureIdentity identity, string scheme)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, identity.Role), new("session_id", identity.Session.ToString()) };
        if (identity.Profile is { } profile) claims.Add(new("player_profile_id", profile.ToString()));
        if (identity.Player is { } player) claims.Add(new("player_id", player.ToString()));
        if (identity.Credential is { } credential) claims.Add(new("player_device_id", credential.ToString()));
        if (identity.Display is { } display) claims.Add(new("device_id", display.ToString()));
        return new(new ClaimsIdentity(claims, scheme));
    }
    private bool StillMatches(SecureIdentity identity) => Capture(Principal(identity, "snapshot")) == identity;
    private SecureGrantState? Read() => failed ? null : persistence.Read();
    private void Save(SecureGrantState state, bool recovery = false)
    {
        try { persistence.Write(state, recovery); failed = false; }
        catch (SecureAccessUnavailable) { failed = true; throw; }
    }
    private void Cleanup()
    {
        foreach (var item in pending.Values.Where(p => p.Expires <= Now || p.Expires > Now.AddMinutes(5) || p.Attempts >= 5).ToArray()) pending.Remove(item.Id);
        foreach (var key in rates.Where(p => p.Value.Start.AddMinutes(1) <= Now).Select(p => p.Key).ToArray()) rates.Remove(key);
    }
    private void Rate(string key)
    {
        Cleanup();
        if (!rates.TryGetValue(key, out var bucket))
        {
            if (rates.Count >= 128) throw new SecureAccessDenied();
            bucket = (Now, 0);
        }
        if (bucket.Count >= 10) throw new SecureAccessDenied();
        rates[key] = (bucket.Start, bucket.Count + 1);
    }

    public SecureEnrollment Begin(string purpose, string browser, string origin, string remote, ClaimsPrincipal user, bool physical = false)
    {
        lock (gate)
        {
            Rate((physical ? "physical-begin:" : "begin:") + remote);
            if (pending.Count >= 32 && physical) pending.Remove(pending.Values.MinBy(p => p.Expires)!.Id);
            if (pending.Count >= 32 || browser.Length != 64) throw new SecureAccessDenied();
            var identity = purpose is "dm-bootstrap" or "dm-recover" ? Capture(user, true) : Capture(user);
            if (identity is null || (purpose == "enroll-player" && identity.Role != "Player") ||
                (purpose == "enroll-display" && identity.Role != "Display") ||
                purpose is not ("dm-bootstrap" or "dm-recover" or "enroll-player" or "enroll-display") ||
                (purpose != "dm-recover" && Read() is null)) throw new SecureAccessDenied();
            foreach (var old in pending.Values.Where(x => x.Browser == browser).ToArray()) pending.Remove(old.Id);
            var request = new SecureEnrollment(Guid.NewGuid(), browser[..32], purpose, identity, browser, origin,
                Now.AddMinutes(5), Convert.ToHexString(RandomNumberGenerator.GetBytes(8)));
            pending.Add(request.Id, request);
            return Public(request);
        }
    }
    private static SecureEnrollment Public(SecureEnrollment request) => request with { Code = "", Browser = "", Identity = request.Identity with { Stamp = "" } };
    private SecureEnrollment? FindBrowser(string browser) { Cleanup(); return pending.Values.SingleOrDefault(p => p.Browser == browser); }
    public SecureEnrollment? ForBrowser(string browser) { lock (gate) return FindBrowser(browser) is { } request ? Public(request) : null; }
    public IReadOnlyList<SecureEnrollment> PhysicalRequests() { lock (gate) { Cleanup(); return pending.Values.Where(p => p.Purpose.StartsWith("dm-")).ToArray(); } }
    public IReadOnlyList<SecureEnrollment> MemberRequests() { lock (gate) { Cleanup(); return pending.Values.Where(p => p.Purpose.StartsWith("enroll-")).Select(Public).ToArray(); } }
    public void ClearPending() { lock (gate) { pending.Clear(); rates.Clear(); } }
    public IReadOnlyList<SecureGrant> Grants() { lock (gate) return Read()?.Grants.Where(g => !g.Revoked && g.Expires > Now).ToArray() ?? []; }

    private SecureGrant Issue(SecureGrantState state, SecureEnrollment request)
    {
        state.Grants.RemoveAll(g => g.Expires <= Now);
        if (state.Grants.Count >= 256) throw new SecureAccessDenied();
        var grant = new SecureGrant(Guid.NewGuid(), request.Identity, request.Browser, request.Origin, state.Epoch, 1, Now.AddHours(12));
        state.Grants.Add(grant);
        return grant;
    }
    public SecureGrant RedeemDm(string browser, string origin, string supplied, string remote)
    {
        lock (gate)
        {
            Rate("redeem:" + remote);
            var request = FindBrowser(browser);
            if (request is null || !request.Purpose.StartsWith("dm-") || request.Origin != origin || !StillMatches(request.Identity)) throw new SecureAccessDenied();
            var canonical = supplied.Replace(" ", "").Replace("-", "").ToUpperInvariant();
            if (canonical.Length != 16 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(request.Code), Encoding.ASCII.GetBytes(canonical)))
            { pending[request.Id] = request with { Attempts = request.Attempts + 1 }; throw new SecureAccessDenied(); }
            pending.Remove(request.Id); // Consume before persistence; a failed write never leaves redeemable proof.
            var recovery = request.Purpose == "dm-recover";
            var state = recovery ? new SecureGrantState() : Read() ?? throw new SecureAccessDenied();
            if (recovery) pending.Clear();
            var grant = Issue(state, request);
            Save(state, recovery);
            return grant;
        }
    }
    public SecureGrant? Validate(Guid id, int generation, Guid epoch, string browser, string origin)
    {
        lock (gate)
        {
            var state = Read();
            var grant = state?.Grants.SingleOrDefault(g => g.Id == id);
            if (grant is null || grant.Revoked || grant.Epoch != epoch || state!.Epoch != epoch || grant.Generation != generation ||
                grant.Expires <= Now || grant.Expires > Now.AddHours(12) || grant.Browser != browser || grant.Origin != origin) return null;
            if (StillMatches(grant.Identity)) return grant;
            state.Grants[state.Grants.IndexOf(grant)] = grant with { Revoked = true, Generation = grant.Generation + 1 };
            Save(state);
            return null;
        }
    }
    public bool MatchesLegacy(SecureGrant grant, ClaimsPrincipal legacy) => Capture(legacy) == grant.Identity;
    public void Approve(SecureGrant dm, Guid id)
    {
        lock (gate)
        {
            Cleanup();
            if (dm.Identity.Role != "DM" || Validate(dm.Id, dm.Generation, dm.Epoch, dm.Browser, dm.Origin) is null ||
                !pending.TryGetValue(id, out var request) || !request.Purpose.StartsWith("enroll-") ||
                request.Origin != dm.Origin || request.Identity.Session != dm.Identity.Session || !StillMatches(request.Identity)) throw new SecureAccessDenied();
            pending[id] = request with { Approver = dm.Id, ApproverGeneration = dm.Generation };
        }
    }
    public SecureGrant Finish(string browser, string origin, ClaimsPrincipal legacy)
    {
        lock (gate)
        {
            var request = FindBrowser(browser);
            var state = Read();
            var approver = state?.Grants.SingleOrDefault(g => g.Id == request?.Approver);
            if (request is null || state is null || request.Origin != origin || Capture(legacy) != request.Identity || approver is null ||
                approver.Identity.Role != "DM" || approver.Generation != request.ApproverGeneration ||
                Validate(approver.Id, approver.Generation, approver.Epoch, approver.Browser, origin) is null) throw new SecureAccessDenied();
            pending.Remove(request.Id);
            var grant = Issue(state, request);
            Save(state);
            return grant;
        }
    }
    public void Cancel(string browser)
    {
        lock (gate)
        {
            foreach (var item in pending.Values.Where(p => p.Browser == browser).ToArray()) pending.Remove(item.Id);
            var state = Read();
            if (state is null) return;
            var changed = false;
            for (var i = 0; i < state.Grants.Count; i++)
                if (!state.Grants[i].Revoked && state.Grants[i].Browser == browser)
                { state.Grants[i] = state.Grants[i] with { Revoked = true, Generation = state.Grants[i].Generation + 1 }; changed = true; }
            if (changed) Save(state);
        }
    }
    public void Revoke(SecureGrant actor, Guid id)
    {
        lock (gate)
        {
            if (Validate(actor.Id, actor.Generation, actor.Epoch, actor.Browser, actor.Origin) is null || (actor.Identity.Role != "DM" && actor.Id != id)) throw new SecureAccessDenied();
            var state = Read() ?? throw new SecureAccessDenied();
            var index = state.Grants.FindIndex(g => g.Id == id);
            if (index < 0) throw new SecureAccessDenied();
            state.Grants[index] = state.Grants[index] with { Revoked = true, Generation = state.Grants[index].Generation + 1 };
            Save(state);
        }
    }
    public void InvalidateIdentities(IEnumerable<Guid> identifiers)
    {
        lock (gate)
        {
            var state = Read();
            if (state is null) return; // Already fails closed until a fresh physical recovery creates a new epoch.
            var ids = identifiers.ToHashSet();
            using var db = factory.CreateDbContext();
            foreach (var id in ids.ToArray())
            {
                var player = db.PlayerConnections.AsNoTracking().SingleOrDefault(p => p.Id == id);
                if (player is not null) ids.Add(player.PlayerProfileId);
                var credential = db.PlayerDeviceCredentials.AsNoTracking().SingleOrDefault(p => p.Id == id);
                if (credential is not null) ids.Add(credential.PlayerProfileId);
                var character = db.PlayerCharacters.AsNoTracking().SingleOrDefault(p => p.Id == id);
                if (character is not null) ids.Add(character.PlayerProfileId);
            }
            foreach (var request in pending.Values.Where(p => ids.Contains(p.Identity.Profile ?? Guid.Empty) || ids.Contains(p.Identity.Display ?? Guid.Empty)).ToArray()) pending.Remove(request.Id);
            var changed = false;
            for (var i = 0; i < state.Grants.Count; i++)
            {
                var grant = state.Grants[i];
                if (!grant.Revoked && (ids.Contains(grant.Identity.Profile ?? Guid.Empty) || ids.Contains(grant.Identity.Display ?? Guid.Empty)))
                { state.Grants[i] = grant with { Revoked = true, Generation = grant.Generation + 1 }; changed = true; }
            }
            if (changed) Save(state);
        }
    }
    public bool MediaOwnershipUnchanged(HttpContext context)
    {
        if (context.Request.RouteValues["scope"]?.ToString() != "character" ||
            !Guid.TryParse(context.Request.RouteValues["subjectId"]?.ToString(), out var id)) return true;
        using var db = factory.CreateDbContext();
        var character = db.PlayerCharacters.AsNoTracking().SingleOrDefault(c => c.Id == id && c.IsActive);
        if (character is null) return false;
        // Frozen N9's asset owner is an immutable upload-time provenance field. Do not follow
        // a later character reassociation merely because its current owner changed.
        var assetType = db.Model.GetEntityTypes().SingleOrDefault(t => t.ClrType.Name == "PresentationAsset");
        if (assetType is null) return true;
        if (assetType.GetTableName() != "PresentationAssets") return false;
        var owners = db.Database.SqlQueryRaw<Guid?>("SELECT \"OwnerProfileId\" AS \"Value\" FROM \"PresentationAssets\" WHERE \"Scope\" = 'character' AND \"SubjectId\" = {0}", id).ToArray();
        return owners.All(owner => owner == character.PlayerProfileId);
    }
}
