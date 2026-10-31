using InitiativeTracker.Core.Coordination;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography.X509Certificates;

namespace InitiativeTracker.Web.Services;

public interface ICoordinationNetwork
{
    Task<(WifiProfile Target, WifiProfile Recovery)> ProfilesAsync(string kind, string? uuid, CancellationToken ct);
    Task<bool> ScheduleAsync(Transition transition, CancellationToken ct);
    Task<bool> ConfirmAsync(Transition transition, CancellationToken ct);
}

public sealed class DeviceCoordinationService
{
    public sealed record Journal(long Revision, Enrollment[] Enrollments, Transition? Transition, string? ProtectedProfiles);
    private readonly object gate = new();
    private readonly string path;
    private readonly IDataProtector protector;
    private readonly HubIdentityService hub;
    private readonly ICoordinationNetwork network;
    private Journal state;
    private readonly Dictionary<Guid, DateTimeOffset> seen = [];
    public bool Enabled { get; }
    public DeviceCoordinationService(IConfiguration configuration, IWebHostEnvironment environment, IDataProtectionProvider protection,
        HubIdentityService hub, ICoordinationNetwork network)
    {
        this.hub = hub; this.network = network;
        Enabled = configuration.GetValue<bool>("Coordination:Enabled");
        var directory = CoordinationEndpoints.DataDirectory(configuration, environment);
        path = Path.Combine(directory, "coordination", "hub.json");
        protector = protection.CreateProtector("InitiativeTracker.DeviceCoordination.v1");
        state = Enabled ? PrivateFile.Read<Journal>(path) ?? new(0, [], null, null) : new(0, [], null, null);
    }
    private void Save(Journal next) { PrivateFile.Save(path, next); state = next; }
    public Journal Snapshot { get { lock (gate) return state with { ProtectedProfiles = null }; } }
    public Task<(WifiProfile Target, WifiProfile Recovery)> RecoveryAsync(CancellationToken ct) => network.ProfilesAsync("hotspot", null, ct);
    public bool Online(Guid id) { lock (gate) return seen.TryGetValue(id, out var at) && at > DateTimeOffset.UtcNow.AddSeconds(-30); }
    public Enrollment Request(X509Certificate2 certificate, EnrollmentRequest request, DateTimeOffset now)
    {
        if (!Enabled || request.Version != DeviceProtocol.Version || request.DeviceId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 64 || request.Name.Any(char.IsControl) ||
            request.Capabilities is null || !DeviceProtocol.Capabilities.All(request.Capabilities.Contains))
            throw new InvalidOperationException("Unsupported device or protocol");
        var pin = DeviceProtocol.Fingerprint(certificate);
        lock (gate)
        {
            var existing = state.Enrollments.SingleOrDefault(x => x.DeviceId == request.DeviceId || x.CertificatePin == pin);
            if (existing is not null && existing.CertificatePin == pin && existing.DeviceId == request.DeviceId)
            {
                if (!existing.Revoked && (existing.Approved || existing.RequestedUtc > now.AddMinutes(-5))) return existing;
                var renewed = existing with { Generation = Guid.NewGuid(), RequestedUtc = now, Revoked = false };
                Save(state with { Enrollments = state.Enrollments.Select(x => x.DeviceId == existing.DeviceId ? renewed : x).ToArray() }); return renewed;
            }
            if (existing is not null) throw new InvalidOperationException("Device identity conflict");
            var retained = state.Enrollments.Where(x => x.Approved || TransitionRules.Pending(state.Transition) && state.Transition!.Devices.Any(d => d.DeviceId == x.DeviceId) || x.RequestedUtc > now.AddMinutes(-5)).ToArray();
            if (retained.Length >= 32) throw new InvalidOperationException("Enrollment queue full");
            var enrollment = new Enrollment(request.DeviceId, request.Name, pin, Guid.NewGuid(), DeviceProtocol.Capabilities, now, false);
            Save(state with { Enrollments = [.. retained, enrollment] }); return enrollment;
        }
    }
    public void Approve(Guid id, string physicallyVerifiedPin, DateTimeOffset now)
    {
        lock (gate)
        {
            var device = state.Enrollments.Single(x => x.DeviceId == id && !x.Approved && !x.Revoked);
            if (device.RequestedUtc <= now.AddMinutes(-5) || device.CertificatePin != physicallyVerifiedPin.ToUpperInvariant())
                throw new InvalidOperationException("Expired request or certificate mismatch");
            Save(state with { Enrollments = state.Enrollments.Select(x => x.DeviceId == id ? x with { Approved = true } : x).ToArray() });
        }
    }
    public void Revoke(Guid id)
    {
        lock (gate)
        {
            Save(state with { Enrollments = state.Enrollments.Select(x => x.DeviceId == id ? x with { Approved = false, Revoked = true } : x).ToArray(),
                Transition = state.Transition is null ? null : state.Transition with { Devices = state.Transition.Devices.Select(d => d.DeviceId == id ? d with { Ready = false, Failure = "Enrollment revoked" } : d).ToArray() } });
            seen.Remove(id);
        }
    }
    public Enrollment Authenticate(X509Certificate2? certificate)
    {
        lock (gate)
        {
            if (!Enabled || certificate is null || DateTime.UtcNow < certificate.NotBefore || DateTime.UtcNow > certificate.NotAfter)
                throw new UnauthorizedAccessException();
            return state.Enrollments.SingleOrDefault(x => x.Approved && DeviceProtocol.PinMatches(certificate, x.CertificatePin))
                ?? throw new UnauthorizedAccessException();
        }
    }
    public DeviceStatus Status(X509Certificate2? certificate)
    {
        lock (gate)
        {
            var device = Authenticate(certificate); seen[device.DeviceId] = DateTimeOffset.UtcNow;
            TransitionPlan? plan = null;
            var t = state.Transition;
            if (t is not null && t.Devices.Any(x => x.DeviceId == device.DeviceId && x.Generation == device.Generation) &&
                !t.LeftBehind.Contains(device.DeviceId) && state.ProtectedProfiles is not null)
            {
                var profiles = System.Text.Json.JsonSerializer.Deserialize<WifiProfile[]>(protector.Unprotect(state.ProtectedProfiles))!;
                plan = new(DeviceProtocol.Version, t, device.DeviceId, device.Generation, profiles[0], profiles[1]);
            }
            return new(DeviceProtocol.Version, hub.HubId, device.Generation, plan, hub.MdnsHostName);
        }
    }
    public async Task PrepareAsync(string kind, string? uuid, CancellationToken ct)
    {
        if (!Enabled || kind is not ("wifi" or "hotspot") || (kind == "wifi" && !Guid.TryParseExact(uuid, "D", out _)))
            throw new InvalidOperationException("Choose a valid destination");
        lock (gate) { if (TransitionRules.Pending(state.Transition)) throw new InvalidOperationException("Transition already pending"); }
        var profiles = await network.ProfilesAsync(kind, uuid, ct);
        lock (gate)
        {
            if (TransitionRules.Pending(state.Transition)) throw new InvalidOperationException("Transition already pending");
            var revision = checked(state.Revision + 1);
            var transition = new Transition(Guid.NewGuid(), revision, hub.HubId, kind, uuid, DateTimeOffset.UtcNow.AddMinutes(2),
                TransitionStage.Preparing, null, null, state.Enrollments.Where(x => x.Approved).Select(x => new DeviceProgress(x.DeviceId, x.Generation)).ToArray(), []);
            Save(state with { Revision = revision, Transition = transition,
                ProtectedProfiles = protector.Protect(System.Text.Json.JsonSerializer.Serialize(new[] { profiles.Target, profiles.Recovery })) });
        }
    }
    public void Receipt(X509Certificate2? certificate, DeviceReceipt receipt, DateTimeOffset now)
    {
        lock (gate)
        {
            var device = Authenticate(certificate); var t = state.Transition ?? throw new InvalidOperationException("No transition");
            if (receipt.Version != DeviceProtocol.Version || receipt.Generation != device.Generation || receipt.TransitionId != t.Id || receipt.Revision != t.Revision ||
                t.LeftBehind.Contains(device.DeviceId)) throw new InvalidOperationException("Stale receipt");
            var progress = t.Devices.Single(x => x.DeviceId == device.DeviceId && x.Generation == device.Generation);
            progress = receipt.Stage switch
            {
                "ready" when t.Stage == TransitionStage.Preparing && now < t.PrepareExpiresUtc => progress with { Ready = true },
                "committed" when t.Stage is TransitionStage.Committed or TransitionStage.HubConfirmed && now < t.DeadlineUtc && progress.Ready => progress with { CommitReceived = true },
                "reconnected" when t.Stage is TransitionStage.Committed or TransitionStage.HubConfirmed && now >= t.SwitchUtc && now < t.DeadlineUtc && progress.CommitReceived => progress with { Reconnected = true },
                "finalized" when t.Stage == TransitionStage.HubConfirmed && now < t.DeadlineUtc && progress.Reconnected => progress with { Finalized = true },
                _ => throw new InvalidOperationException("Out of order or expired receipt")
            };
            Save(state with { Transition = t with { Devices = t.Devices.Select(x => x.DeviceId == device.DeviceId ? progress : x).ToArray() } });
        }
    }
    public void Commit(Guid id, Guid[] leaveBehind, DateTimeOffset now)
    {
        lock (gate)
        {
            var t = state.Transition ?? throw new InvalidOperationException("No transition");
            if (t.Id != id || t.Stage != TransitionStage.Preparing || now >= t.PrepareExpiresUtc) throw new InvalidOperationException("Preparation expired");
            var unready = t.Devices.Where(x => !x.Ready || !Online(x.DeviceId) || !state.Enrollments.Any(e => e.Approved && e.DeviceId == x.DeviceId && e.Generation == x.Generation)).Select(x => x.DeviceId).ToArray();
            if (leaveBehind.Except(t.Devices.Select(x => x.DeviceId)).Any() || unready.Except(leaveBehind).Any())
                throw new InvalidOperationException("Explicitly identify every unavailable device to leave behind, or postpone");
            Save(state with { Transition = t with { Stage = TransitionStage.Committed, LeftBehind = leaveBehind.Distinct().ToArray(),
                SwitchUtc = now.AddSeconds(10), DeadlineUtc = now.AddSeconds(190) } });
        }
    }
    public void Postpone(Guid id)
    {
        lock (gate)
        {
            if (state.Transition?.Id != id || state.Transition.Stage != TransitionStage.Preparing) throw new InvalidOperationException("Committed transitions cannot be cancelled");
            Save(state with { Transition = state.Transition with { Stage = TransitionStage.Expired }, ProtectedProfiles = null });
        }
    }
    public async Task ConfirmAsync(Guid id, CancellationToken ct)
    {
        Transition t;
        lock (gate)
        {
            t = state.Transition ?? throw new InvalidOperationException("No transition");
            if (t.Id != id || t.Stage != TransitionStage.Committed || DateTimeOffset.UtcNow >= t.DeadlineUtc || DateTimeOffset.UtcNow < t.SwitchUtc)
                throw new InvalidOperationException("Not awaiting hub confirmation");
        }
        if (!await network.ConfirmAsync(t, ct)) throw new InvalidOperationException("Root controller did not confirm the destination");
        lock (gate)
        {
            if (state.Transition?.Id != t.Id || DateTimeOffset.UtcNow >= t.DeadlineUtc) throw new InvalidOperationException("Confirmation deadline expired");
            Save(state with { Transition = state.Transition with { Stage = TransitionStage.HubConfirmed } });
        }
    }
    public async Task TickAsync(CancellationToken ct)
    {
        Transition? t;
        lock (gate)
        {
            t = state.Transition;
            if (t is null) return;
            var now = DateTimeOffset.UtcNow;
            if (t.Stage == TransitionStage.Preparing && now >= t.PrepareExpiresUtc)
            { Save(state with { Transition = t with { Stage = TransitionStage.Expired }, ProtectedProfiles = null }); return; }
            if (t.Stage is TransitionStage.Committed or TransitionStage.HubConfirmed && now >= t.DeadlineUtc)
            { Save(state with { Transition = t with { Stage = t.Stage == TransitionStage.HubConfirmed ? TransitionStage.Completed : TransitionStage.Recovered }, ProtectedProfiles = null }); return; }
        }
        // The root scheduler deduplicates by transaction ID, persists before scheduling, and owns rollback independently.
        if (t.Stage == TransitionStage.Committed && DateTimeOffset.UtcNow < t.SwitchUtc)
            await network.ScheduleAsync(t, ct);
    }
}
