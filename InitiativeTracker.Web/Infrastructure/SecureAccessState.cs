using System.Runtime.InteropServices;
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
// Remembered-browser state contains only a SHA-256 hash of the host-only,
 // Secure/HttpOnly random browser cookie. It never stores a reusable raw token.
public sealed record SecureRememberedBrowser(Guid Id, SecureIdentity Identity, string Browser, string Origin,
    Guid Epoch, int Generation, DateTimeOffset Expires, string Label, bool Revoked = false);
public sealed class SecureGrantState
{
    public int Format { get; set; } = 1;
    public Guid Epoch { get; set; } = Guid.NewGuid();
    public List<SecureGrant> Grants { get; set; } = [];
    // Optional in pre-remembered Format=1 state; absence reads as an empty set.
    public List<SecureRememberedBrowser> RememberedBrowsers { get; set; } = [];
}
public sealed record SecureEnrollment(Guid Id, string Reference, string Purpose, SecureIdentity Identity, string Browser,
    string Origin, DateTimeOffset Expires, string Code, int Attempts = 0, Guid? Approver = null, int ApproverGeneration = 0,
    bool PhysicalApproved = false);
public sealed record SecureAccessStorage(string Root, bool Persistent);
public enum SecurePersistenceSync { StoreDirectory, ParentDirectory, FenceCreated, StateReplaced, FenceRemoved, FailureFence }

internal static class LinuxDirectoryDurability
{
    [DllImport("libc", EntryPoint = "opendir", SetLastError = true)]
    private static extern IntPtr OpenDirectory([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport("libc", EntryPoint = "dirfd", SetLastError = true)]
    private static extern int DirectoryDescriptor(IntPtr directory);
    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Sync(int descriptor);
    [DllImport("libc", EntryPoint = "closedir", SetLastError = true)]
    private static extern int CloseDirectory(IntPtr directory);

    public static void Synchronize(string path)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        // opendir/dirfd avoid architecture-specific open flags on x64 and ARM64.
        var directory = OpenDirectory(path);
        if (directory == IntPtr.Zero) throw new IOException("Durable directory open failed.");
        try
        {
            var descriptor = DirectoryDescriptor(directory);
            if (descriptor < 0) throw new IOException("Durable directory descriptor unavailable.");
            for (var attempt = 0; ; attempt++)
            {
                if (Sync(descriptor) == 0) break;
                if (Marshal.GetLastPInvokeError() != 4 || attempt >= 63) // EINTR; bounded retry.
                    throw new IOException("Durable directory synchronization failed.");
            }
        }
        finally
        {
            if (CloseDirectory(directory) != 0) throw new IOException("Durable directory close failed.");
        }
    }
}

/// <summary>Protected durable state with a write-ahead fence. No automatic legacy enrollment or backup recovery.</summary>
public class SecureGrantPersistence(SecureAccessStorage storage, IDataProtectionProvider protection)
{
    private readonly IDataProtector protector = protection.CreateProtector("InitiativeTracker.SecureAccess.State.v1");
    private bool durabilityReady;
    private bool unavailable;
    private static bool StorageFailure(Exception error) => error is IOException or UnauthorizedAccessException or CryptographicException or
        InvalidOperationException or NotSupportedException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or SecureAccessUnavailable;
    protected virtual void SynchronizeDirectory(string directory, SecurePersistenceSync stage) => LinuxDirectoryDurability.Synchronize(directory);
    private void EnsureDirectoryDurability()
    {
        if (durabilityReady) return;
        // Flush from the store through its ancestors, including any newly created parents.
        // Do not acknowledge an installation on a filesystem that cannot support this.
        for (var directory = new DirectoryInfo(storage.Root); directory is not null; directory = directory.Parent)
            SynchronizeDirectory(directory.FullName, directory.FullName == storage.Root ? SecurePersistenceSync.StoreDirectory : SecurePersistenceSync.ParentDirectory);
        durabilityReady = true;
    }
    public virtual SecureGrantState? Read()
    {
        if (!storage.Persistent || !OperatingSystem.IsLinux() || unavailable) return null;
        var path = Path.Combine(storage.Root, "state.bin");
        if (File.Exists(Path.Combine(storage.Root, "mutation.pending")) || !File.Exists(path)) return null;
        try
        {
            BrowserCertificateStore.CheckPath(storage.Root, true);
            BrowserCertificateStore.CheckPath(path, false);
            EnsureDirectoryDurability();
            if (new FileInfo(path).Length > 1_048_576) return null;
            var plain = protector.Unprotect(File.ReadAllBytes(path));
            using var document = JsonDocument.Parse(plain);
            if (!document.RootElement.TryGetProperty("Format", out _) || !document.RootElement.TryGetProperty("Epoch", out _) ||
                !document.RootElement.TryGetProperty("Grants", out _)) return null;
            var state = JsonSerializer.Deserialize<SecureGrantState>(plain);
            return state is { Format: 1 } && state.Epoch != Guid.Empty && state.Grants is { Count: <= 256 } &&
                state.Grants.All(g => g is not null && g.Identity is not null && g.Id != Guid.Empty && g.Epoch == state.Epoch && g.Generation >= 1 &&
                    g.Browser is { Length: 64 } && g.Identity.Stamp is { Length: 64 } && g.Identity.Role is "DM" or "Player" or "Display") &&
                state.Grants.Select(g => g.Id).Distinct().Count() == state.Grants.Count &&
                state.RememberedBrowsers is { Count: <= 64 } &&
                state.RememberedBrowsers.All(b => b is not null && b.Identity is not null &&
                    b.Id != Guid.Empty && b.Epoch == state.Epoch && b.Generation >= 1 &&
                    b.Browser is { Length: 64 } && b.Browser.All(Uri.IsHexDigit) &&
                    b.Origin is { Length: > 8 and <= 300 } && b.Origin.StartsWith("https://", StringComparison.Ordinal) &&
                    b.Label is { Length: >= 1 and <= 48 } && !b.Label.Any(char.IsControl) &&
                    b.Identity.Stamp is { Length: 64 } && b.Identity.Role is "DM" or "Player" or "Display") &&
                state.RememberedBrowsers.Select(b => b.Id).Distinct().Count() == state.RememberedBrowsers.Count &&
                state.RememberedBrowsers.Where(b => !b.Revoked).Select(b => b.Browser).Distinct().Count() ==
                    state.RememberedBrowsers.Count(b => !b.Revoked) ? state : null;
        }
        catch (Exception e) when (StorageFailure(e) || e is JsonException)
        { return null; }
    }
    public virtual void Write(SecureGrantState state, bool recovery = false)
    {
        if (!storage.Persistent || !OperatingSystem.IsLinux() || unavailable && !recovery) throw new SecureAccessUnavailable();
        try
        {
            Directory.CreateDirectory(storage.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            BrowserCertificateStore.CheckPath(storage.Root, true);
            EnsureDirectoryDurability();
            var fence = Path.Combine(storage.Root, "mutation.pending");
            if (File.Exists(fence) && !recovery) throw new SecureAccessUnavailable();
            if (File.Exists(fence)) BrowserCertificateStore.CheckPath(fence, false);
            WriteFence(fence);
            SynchronizeDirectory(storage.Root, SecurePersistenceSync.FenceCreated);
            var bytes = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(state));
            if (bytes.Length > 1_048_576) throw new SecureAccessUnavailable();
            var temporary = Path.Combine(storage.Root, ".state-" + Guid.NewGuid().ToString("N"));
            using (var output = PrivateFile(temporary, FileMode.CreateNew)) { output.Write(bytes); output.Flush(true); }
            File.Move(temporary, Path.Combine(storage.Root, "state.bin"), true);
            SynchronizeDirectory(storage.Root, SecurePersistenceSync.StateReplaced);
            File.Delete(fence);
            SynchronizeDirectory(storage.Root, SecurePersistenceSync.FenceRemoved);
            unavailable = false;
        }
        catch (Exception e) when (StorageFailure(e))
        {
            unavailable = true;
            durabilityReady = false;
            // A failed final sync may follow an unlink visible in memory. Re-establish a
            // fence when storage permits it. Failure here remains explicitly unconfirmed.
            try
            {
                BrowserCertificateStore.CheckPath(storage.Root, true);
                var fence = Path.Combine(storage.Root, "mutation.pending");
                if (File.Exists(fence)) BrowserCertificateStore.CheckPath(fence, false);
                WriteFence(fence);
                SynchronizeDirectory(storage.Root, SecurePersistenceSync.FailureFence);
            }
            catch (Exception failure) when (StorageFailure(failure)) { }
            throw new SecureAccessUnavailable();
        }
    }
    private static void WriteFence(string fence)
    {
        using var marker = PrivateFile(fence, FileMode.Create);
        marker.WriteByte(1);
        marker.Flush(true);
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
    // Only the protected physical HTTPS endpoint exposes this queue. Member
    // codes are not used: browser-bound completion requires explicit consent.
    public IReadOnlyList<SecureEnrollment> PhysicalRequests()
    {
        lock (gate)
        {
            Cleanup();
            return pending.Values.Where(p => p.Purpose.StartsWith("dm-") || p.Purpose.StartsWith("enroll-")).ToArray();
        }
    }
    // A physical tap approves only the exact, short-lived request. The browser
    // must independently redeem its own browser-bound challenge; a physical
    // response never issues a cookie or transfers proof to another browser.
    public void ApprovePhysicalDm(Guid id)
    {
        lock (gate)
        {
            Cleanup();
            if (!pending.TryGetValue(id, out var request) || !request.Purpose.StartsWith("dm-") ||
                !StillMatches(request.Identity) || request.PhysicalApproved)
                throw new SecureAccessDenied();
            pending[id] = request with { PhysicalApproved = true };
        }
    }
    public void DenyPhysicalDm(Guid id)
    {
        lock (gate)
        {
            Cleanup();
            if (!pending.TryGetValue(id, out var request) || !request.Purpose.StartsWith("dm-"))
                throw new SecureAccessDenied();
            pending.Remove(id);
        }
    }
    // The physical Razor handler has already verified canonical TLS, exact
    // socket provenance, Origin, and antiforgery before calling this method.
    // A physical decision marks a request only; it never creates a grant.
    public void ApprovePhysicalRequest(Guid id)
    {
        lock (gate)
        {
            Cleanup();
            if (!pending.TryGetValue(id, out var request) ||
                request.Purpose is not ("dm-bootstrap" or "dm-recover" or "enroll-player" or "enroll-display") ||
                request.PhysicalApproved || !StillMatches(request.Identity) ||
                (request.Purpose.StartsWith("enroll-") && Read() is null))
                throw new SecureAccessDenied();
            pending[id] = request with { PhysicalApproved = true };
        }
    }
    public void DenyPhysicalRequest(Guid id)
    {
        lock (gate)
        {
            Cleanup();
            if (!pending.TryGetValue(id, out var request) ||
                request.Purpose is not ("dm-bootstrap" or "dm-recover" or "enroll-player" or "enroll-display"))
                throw new SecureAccessDenied();
            pending.Remove(id);
        }
    }
    public SecureGrant FinishPhysicalDm(string browser, string origin)
    {
        lock (gate)
        {
            var request = FindBrowser(browser);
            if (request is null || !request.Purpose.StartsWith("dm-") || !request.PhysicalApproved ||
                request.Origin != origin || !StillMatches(request.Identity)) throw new SecureAccessDenied();
            pending.Remove(request.Id); // The approval is one use, even on write failure.
            var recovery = request.Purpose == "dm-recover";
            var state = recovery ? new SecureGrantState() : Read() ?? throw new SecureAccessDenied();
            if (recovery) pending.Clear();
            var grant = Issue(state, request);
            Save(state, recovery);
            return grant;
        }
    }
    public IReadOnlyList<SecureEnrollment> MemberRequests() { lock (gate) { Cleanup(); return pending.Values.Where(p => p.Purpose.StartsWith("enroll-")).Select(Public).ToArray(); } }
    public void ClearPending() { lock (gate) { pending.Clear(); rates.Clear(); } }
    public IReadOnlyList<SecureGrant> Grants() { lock (gate) return Read()?.Grants.Where(g => !g.Revoked && g.Expires > Now).ToArray() ?? []; }

    public IReadOnlyList<SecureRememberedBrowser> Remembered(SecureGrant actor)
    {
        lock (gate)
        {
            if (Validate(actor.Id, actor.Generation, actor.Epoch, actor.Browser, actor.Origin) is null)
                throw new SecureAccessDenied();
            var state = Read() ?? throw new SecureAccessUnavailable();
            return state.RememberedBrowsers
                .Where(b => !b.Revoked && b.Expires > Now &&
                    (actor.Identity.Role == "DM" || b.Identity == actor.Identity))
                .OrderBy(b => b.Identity.Role).ThenBy(b => b.Label).ToArray();
        }
    }

    // A user opts in only after fresh secure approval. Browser cookie remains a
    // random host-only HttpOnly Secure credential; persistence stores its hash.
    // Trust expires absolutely after 30 days, without sliding extension.
    public SecureRememberedBrowser Remember(SecureGrant actor, string? label)
    {
        lock (gate)
        {
            if (Validate(actor.Id, actor.Generation, actor.Epoch, actor.Browser, actor.Origin) is null)
                throw new SecureAccessDenied();
            label = (label ?? "").Trim();
            if (label.Length is < 1 or > 48 || label.Any(char.IsControl))
                throw new SecureAccessDenied();
            var state = Read() ?? throw new SecureAccessUnavailable();
            state.RememberedBrowsers.RemoveAll(b => b.Revoked || b.Expires <= Now || b.Browser == actor.Browser);
            if (state.RememberedBrowsers.Count >= 64) throw new SecureAccessDenied();
            var trust = new SecureRememberedBrowser(Guid.NewGuid(), actor.Identity, actor.Browser, actor.Origin,
                state.Epoch, 1, Now.AddDays(30), label);
            state.RememberedBrowsers.Add(trust);
            Save(state);
            return trust;
        }
    }

    // A remembered browser may restore only its original immutable role,
    // hub/session, credential snapshot and trusted TLS origin. It cannot bypass
    // revocation or impersonate a different already-signed-in legacy principal.
    public SecureGrant? ResumeRemembered(string browser, string origin, ClaimsPrincipal legacy)
    {
        lock (gate)
        {
            if (browser.Length != 64 || !browser.All(Uri.IsHexDigit)) return null;
            var state = Read();
            var matches = state?.RememberedBrowsers.Where(b => !b.Revoked && b.Browser == browser &&
                b.Origin == origin && b.Epoch == state.Epoch && b.Expires > Now &&
                b.Expires <= Now.AddDays(30)).ToArray();
            if (matches is not { Length: 1 }) return null;
            var trust = matches[0];
            if (!StillMatches(trust.Identity))
            {
                state!.RememberedBrowsers[state.RememberedBrowsers.IndexOf(trust)] =
                    trust with { Revoked = true, Generation = trust.Generation + 1 };
                Save(state);
                return null;
            }
            if (legacy.Identity?.IsAuthenticated == true && Capture(legacy) != trust.Identity)
                return null;
            var existing = state!.Grants.FirstOrDefault(g => !g.Revoked && g.Epoch == state.Epoch &&
                g.Identity == trust.Identity && g.Browser == browser && g.Origin == origin && g.Expires > Now);
            if (existing is not null) return existing;
            var request = new SecureEnrollment(Guid.NewGuid(), browser[..32], "remembered-return",
                trust.Identity, browser, origin, Now.AddMinutes(5), "");
            var grant = Issue(state, request);
            Save(state);
            return grant;
        }
    }

    public void RevokeRemembered(SecureGrant actor, Guid id)
    {
        lock (gate)
        {
            if (Validate(actor.Id, actor.Generation, actor.Epoch, actor.Browser, actor.Origin) is null)
                throw new SecureAccessDenied();
            var state = Read() ?? throw new SecureAccessUnavailable();
            var index = state.RememberedBrowsers.FindIndex(b => b.Id == id && !b.Revoked && b.Expires > Now);
            if (index < 0) throw new SecureAccessDenied();
            var target = state.RememberedBrowsers[index];
            if (actor.Identity.Role != "DM" && actor.Identity != target.Identity)
                throw new SecureAccessDenied();
            state.RememberedBrowsers[index] = target with { Revoked = true, Generation = target.Generation + 1 };
            for (var i = 0; i < state.Grants.Count; i++)
                if (!state.Grants[i].Revoked && state.Grants[i].Browser == target.Browser &&
                    state.Grants[i].Origin == target.Origin && state.Grants[i].Identity == target.Identity)
                    state.Grants[i] = state.Grants[i] with {
                        Revoked = true, Generation = state.Grants[i].Generation + 1
                    };
            Save(state);
        }
    }

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
    /// <summary>
    /// Serializes a prepared mutation's final synchronous database commit with revocation.
    /// The caller must first enforce trusted transport, CSRF and asset/role permissions.
    /// No decoding, file I/O, quota scan, asynchronous work or pre-held write transaction
    /// belongs in this callback. Its database operation must have a bounded timeout.
    /// Expiry linearizes at the last check immediately before callback entry; a commit
    /// admitted before expiry may finish afterward. This does not extend the grant.
    /// </summary>
    public int CommitAuthorized(SecureGrant expected, string browser, string origin,
        ClaimsPrincipal matchingLegacyPrincipal, Func<int> commit)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(matchingLegacyPrincipal);
        ArgumentNullException.ThrowIfNull(commit);
        if (!Monitor.TryEnter(gate, millisecondsTimeout: 250)) throw new SecureAccessUnavailable();
        try
        {
            var current = Validate(expected.Id, expected.Generation, expected.Epoch, browser, origin);
            if (current is null || current != expected || current.Identity.Role is not ("DM" or "Player") ||
                !MatchesLegacy(current, matchingLegacyPrincipal)) throw new SecureAccessDenied();
            // Identity/database reads can take time: check the clock again after them,
            // while still excluding revoke/cancel and immediately before invoking commit.
            var linearizationTime = Now;
            if (current.Expires <= linearizationTime || current.Expires > linearizationTime.AddHours(12))
                throw new SecureAccessDenied();
            return commit();
        }
        finally { Monitor.Exit(gate); }
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
            if (request is null || !request.Purpose.StartsWith("enroll-") || state is null || request.Origin != origin ||
                Capture(legacy) != request.Identity || !StillMatches(request.Identity) ||
                (!request.PhysicalApproved && (approver is null || approver.Identity.Role != "DM" ||
                    approver.Generation != request.ApproverGeneration ||
                    Validate(approver.Id, approver.Generation, approver.Epoch, approver.Browser, origin) is null)))
                throw new SecureAccessDenied();
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
