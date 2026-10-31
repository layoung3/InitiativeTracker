using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using InitiativeTracker.Core.Coordination;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace InitiativeTracker.Satellite;

public sealed record Candidate(string Url, string Pin, Guid HubId, Guid Generation, string DisplayUrl, string Name, DateTimeOffset ExpiresUtc);
public sealed record AgentState(Guid DeviceId, DeviceBinding? Binding, Candidate? Candidate, long LastRevision, Transition? Transition,
    DateTimeOffset? UnavailableSince, int Failures, DateTimeOffset? NextRecoveryUtc, int RecoveryAttempts, string OwnerHash);

public sealed class SatelliteAgent
{
    private readonly string path;
    private readonly IConfiguration configuration;
    private readonly TimeProvider time;
    private AgentState state;
    private readonly SemaphoreSlim gate = new(1);
    private HubConnection? connection;
    private string? connectedTo;
    private long? firstFailureTimestamp;
    private readonly string kioskPath;
    public X509Certificate2 Certificate { get; }
    public string Pin => DeviceProtocol.Fingerprint(Certificate);
    public AgentState Snapshot => state;
    public string? SetupOwnerCredential { get; private set; }
    public string Message { get; private set; } = "Ready";
    public SatelliteAgent(IConfiguration configuration, TimeProvider? timeProvider = null)
    {
        this.configuration = configuration;
        time = timeProvider ?? TimeProvider.System;
        var directory = configuration["Satellite:StateDirectory"] ?? "/var/lib/initiativetracker-satellite";
        PrivateFile.Directory(directory);
        path = Path.Combine(directory, "agent.json");
        kioskPath = configuration["Satellite:KioskState"] ?? "/run/initiativetracker-satellite/kiosk.json";
        Certificate = DeviceProtocol.Certificate(Path.Combine(directory, "agent.pfx"), "InitiativeTracker satellite", false);
        var saved = PrivateFile.Read<AgentState>(path);
        if (saved is null)
        {
            SetupOwnerCredential = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            state = new(Guid.NewGuid(), null, null, 0, null, null, 0, null, 0, Hash(SetupOwnerCredential));
            Save(state);
            PrivateFile.Save(Path.Combine(directory, "owner-recovery.json"), new { credential = SetupOwnerCredential });
        }
        else state = saved;
    }
    public static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret)));
    public bool Owner(string? credential) => credential is { Length: 64 } && CryptographicOperations.FixedTimeEquals(
        System.Text.Encoding.ASCII.GetBytes(Hash(credential)), System.Text.Encoding.ASCII.GetBytes(state.OwnerHash));
    private void Save(AgentState next) { PrivateFile.Save(path, next); state = next; }
    private HttpClient Client(string pin)
    {
        if (pin.Length != 64 || !pin.All(char.IsAsciiHexDigit)) throw new InvalidOperationException("Enter the complete physical hub fingerprint");
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        handler.ClientCertificates.Add(Certificate);
        handler.ServerCertificateCustomValidationCallback = (_, cert, _, _) => DeviceProtocol.PinMatches(cert, pin) && cert!.NotAfter > DateTime.UtcNow && cert.NotBefore <= DateTime.UtcNow;
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
    }
    private static string Origin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("Use the hub's HTTPS origin, for example https://initiative-12345678.local:5443");
        return uri.GetLeftPart(UriPartial.Authority);
    }
    public async Task<JsonElement> Broker(object request, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(55));
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(configuration["Satellite:NetworkSocket"] ?? "/run/initiativetracker-satellite/network.sock"), timeout.Token);
        using var stream = new NetworkStream(socket);
        await stream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)), timeout.Token);
        await stream.WriteAsync("\n"u8.ToArray(), timeout.Token);
        using var reader = new StreamReader(stream);
        var raw = await reader.ReadLineAsync(timeout.Token);
        if (raw is null || raw.Length > 32768) throw new IOException("Invalid network broker response");
        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.GetProperty("ok").GetBoolean()) throw new IOException("Network broker refused operation");
        return doc.RootElement.Clone();
    }
    public async Task RequestPairing(string url, string pin, string name, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            url = Origin(url); pin = pin.ToUpperInvariant();
            using var client = Client(pin);
            var response = await client.PostAsJsonAsync(url + "/api/coordination/enroll", new EnrollmentRequest(state.DeviceId, name, DeviceProtocol.Version, DeviceProtocol.Capabilities), ct);
            response.EnsureSuccessStatusCode();
            var enrollment = await response.Content.ReadFromJsonAsync<Enrollment>(ct) ?? throw new IOException("Invalid enrollment");
            if (enrollment.CertificatePin != Pin || enrollment.DeviceId != state.DeviceId) throw new IOException("Enrollment identity mismatch");
            Save(state with { Candidate = new(url, pin, Guid.Empty, enrollment.Generation,
                new UriBuilder(url) { Scheme = "http", Port = configuration.GetValue<int?>("Satellite:DisplayPort") ?? 5000, Path = "/display", Query = "satellite=1" }.Uri.ToString(), name, time.GetUtcNow().AddMinutes(10)) });
            Message = "Request sent. Approve this satellite certificate at the destination hub's physical DM screen, then finish pairing here.";
        }
        finally { gate.Release(); }
    }
    public async Task FinishPairing(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { await FinishPairingCore(ct); }
        finally { gate.Release(); }
    }
    private async Task FinishPairingCore(CancellationToken ct)
    {
            var candidate = state.Candidate ?? throw new InvalidOperationException("Request pairing first");
            using var client = Client(candidate.Pin);
            var status = await client.GetFromJsonAsync<DeviceStatus>(candidate.Url + "/api/coordination/status", ct) ?? throw new IOException();
            if (status.Version != DeviceProtocol.Version || status.Generation != candidate.Generation || status.HubId == Guid.Empty) throw new IOException("Hub approval missing");
            var bootstrap = await client.GetFromJsonAsync<JsonElement>(candidate.Url + "/api/coordination/recovery", ct);
            if (bootstrap.GetProperty("generation").GetGuid() != candidate.Generation) throw new IOException("Binding changed");
            var recovery = bootstrap.GetProperty("recovery").Deserialize<WifiProfile>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            // Owner explicitly authorized replacement, and new DM approval is now authenticated.
            // Preserve old agent binding until the broker has durably committed the new generation.
            await Broker(new { action = "rebind", generation = candidate.Generation, recovery }, ct);
            var stableUrl = string.IsNullOrWhiteSpace(status.MdnsHost) ? candidate.Url : new UriBuilder(candidate.Url) { Host = status.MdnsHost }.Uri.GetLeftPart(UriPartial.Authority);
            var displayUrl = new UriBuilder(candidate.DisplayUrl) { Host = new Uri(stableUrl).Host }.Uri.ToString();
            Save(state with { Binding = new(status.HubId, state.DeviceId, status.Generation, candidate.Pin, stableUrl, displayUrl),
                Candidate = null, Transition = null, LastRevision = 0, UnavailableSince = null, Failures = 0, RecoveryAttempts = 0 });
            SetupOwnerCredential = null;
            await Disconnect(); Show(displayUrl);
            Message = "Paired network agent. Approve the existing display pairing request separately if this is a new hub.";
    }
    public async Task BeginReassignment(string url, string pin, string name, string? ssid, string? password, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            url = Origin(url); pin = pin.ToUpperInvariant();
            if (pin.Length != 64 || !pin.All(char.IsAsciiHexDigit) || string.IsNullOrWhiteSpace(name) || name.Length > 64)
                throw new InvalidOperationException("Invalid destination identity");
            await Broker(new { action = "open-provisioning" }, ct);
            var display = new UriBuilder(url) { Scheme = "http", Port = configuration.GetValue<int?>("Satellite:DisplayPort") ?? 5000, Path = "/display", Query = "satellite=1" }.Uri.ToString();
            Save(state with { Candidate = new(url, pin, Guid.Empty, Guid.Empty, display, name, time.GetUtcNow().AddMinutes(10)) });
            if (!string.IsNullOrWhiteSpace(ssid)) await ConfigureNetwork(ssid, password ?? "", ct);
            Message = "Reassignment authorized for the specified hub fingerprint only. Approve the satellite fingerprint in that hub's DM interface; setup then finishes automatically.";
        }
        finally { gate.Release(); }
    }
    private async Task AdvanceCandidate(CancellationToken ct)
    {
        var candidate = state.Candidate!;
        if (time.GetUtcNow() >= candidate.ExpiresUtc)
        {
            Save(state with { Candidate = null });
            await Broker(new { action = "close-provisioning" }, ct);
            Message = "Reassignment expired. Previous association retained."; return;
        }
        using var client = Client(candidate.Pin);
        var response = await client.PostAsJsonAsync(candidate.Url + "/api/coordination/enroll",
            new EnrollmentRequest(state.DeviceId, candidate.Name, DeviceProtocol.Version, DeviceProtocol.Capabilities), ct);
        response.EnsureSuccessStatusCode();
        var enrollment = await response.Content.ReadFromJsonAsync<Enrollment>(ct) ?? throw new IOException("Invalid enrollment");
        if (enrollment.DeviceId != state.DeviceId || enrollment.CertificatePin != Pin) throw new IOException("Device identity mismatch");
        Save(state with { Candidate = candidate with { Generation = enrollment.Generation } });
        if (enrollment.Approved) await FinishPairingCore(ct);
    }
    public async Task CancelCandidate(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { Save(state with { Candidate = null }); await Broker(new { action = "close-provisioning" }, ct); if (state.Binding is { } b) Show(b.DisplayUrl); }
        finally { gate.Release(); }
    }
    public async Task Forget(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await Broker(new { action = "reset" }, ct);
            Save(state with { Binding = null, Candidate = null, Transition = null, LastRevision = 0 });
            await Disconnect(); Show("http://127.0.0.1:5081/");
        }
        finally { gate.Release(); }
    }
    public async Task OpenSetup(CancellationToken ct)
    {
        await Broker(new { action = "open-provisioning" }, ct);
        Show("http://127.0.0.1:5081/");
    }
    public async Task ConfigureNetwork(string ssid, string password, CancellationToken ct) =>
        await Broker(new { action = "candidate-network", profile = new WifiProfile(ssid, password) }, ct);
    public void Show(string url, bool force = false)
    {
        // Public kiosk control contains URL only; no credentials, pins or pairing capabilities.
        var directory = Path.GetDirectoryName(kioskPath)!;
        System.IO.Directory.CreateDirectory(directory);
        if (!force && File.Exists(kioskPath))
        {
            using var existing = JsonDocument.Parse(File.ReadAllText(kioskPath));
            if (existing.RootElement.GetProperty("url").GetString() == url) return;
        }
        var temporary = kioskPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { url, revision = Guid.NewGuid() }));
        File.Move(temporary, kioskPath, true);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(kioskPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
    }
    private async Task Disconnect()
    {
        if (connection is not null) await connection.DisposeAsync();
        connection = null; connectedTo = null;
    }
    private async Task<DeviceStatus> Poll(DeviceBinding binding, CancellationToken ct)
    {
        var url = binding.HubUrl;
        var root = await Broker(new { action = "status" }, ct);
        var localState = root.GetProperty("state");
        if (localState.GetProperty("recovery").ValueKind == JsonValueKind.String && root.GetProperty("active").GetString() == localState.GetProperty("recovery").GetString())
            url = new UriBuilder(binding.HubUrl) { Host = "10.84.0.1" }.Uri.GetLeftPart(UriPartial.Authority);
        if (connection is null || connectedTo != url)
        {
            await Disconnect();
            connection = new HubConnectionBuilder().WithUrl(url + "/hubs/device-coordination", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.ClientCertificates ??= new X509CertificateCollection();
                options.ClientCertificates.Add(Certificate);
                options.HttpMessageHandlerFactory = handler =>
                {
                    var http = (HttpClientHandler)handler; http.AllowAutoRedirect = false;
                    if (!http.ClientCertificates.Contains(Certificate)) http.ClientCertificates.Add(Certificate);
                    http.ServerCertificateCustomValidationCallback = (_, cert, _, _) => DeviceProtocol.PinMatches(cert, binding.HubPin) && cert!.NotAfter > DateTime.UtcNow && cert.NotBefore <= DateTime.UtcNow;
                    return http;
                };
            }).Build();
            connection.On("PlanAvailable", () => { });
            connectedTo = url;
        }
        if (connection.State == HubConnectionState.Disconnected) await connection.StartAsync(ct);
        var status = await connection.InvokeAsync<DeviceStatus>("Poll", ct);
        if (status.HubId != binding.HubId || status.Generation != binding.Generation || status.Version != DeviceProtocol.Version)
            throw new IOException("Paired hub identity or binding mismatch");
        return status;
    }
    private Task Receipt(Transition t, string stage, CancellationToken ct) => connection!.InvokeAsync("Receipt",
        new DeviceReceipt(DeviceProtocol.Version, t.Id, t.Revision, state.Binding!.Generation, stage), ct);
    public async Task Process(DeviceStatus status, CancellationToken ct)
    {
        var binding = state.Binding!; var plan = status.Plan;
        if (plan is null) return;
        var t = plan.Transition; var now = time.GetUtcNow();
        var old = state.Transition;
        if (old is null || !TransitionRules.SameTransaction(old, t))
        {
            if (!TransitionRules.ValidPlan(plan, binding, state.LastRevision, now)) return;
            await Broker(new { action = "prepare", generation = binding.Generation, id = t.Id, revision = t.Revision,
                expires = t.PrepareExpiresUtc.ToUnixTimeMilliseconds() / 1000.0, target = plan.Target }, ct);
            Save(state with { Transition = t, LastRevision = t.Revision });
            await Receipt(t, "ready", ct); return;
        }
        if (plan.Generation != binding.Generation || plan.DeviceId != state.DeviceId) throw new IOException("Plan binding mismatch");
        if (t.Stage == TransitionStage.Preparing && old.Stage == TransitionStage.Preparing)
        {
            if (old.TargetKind != t.TargetKind || old.TargetUuid != t.TargetUuid || old.PrepareExpiresUtc != t.PrepareExpiresUtc) throw new IOException("Conflicting preparation");
            if (now < t.PrepareExpiresUtc)
            {
                await Broker(new { action = "prepare", generation = binding.Generation, id = t.Id, revision = t.Revision,
                    expires = t.PrepareExpiresUtc.ToUnixTimeMilliseconds() / 1000.0, target = plan.Target }, ct);
                await Receipt(t, "ready", ct);
            }
            return;
        }
        if (t.Stage is TransitionStage.Expired or TransitionStage.Recovered)
        {
            if (old.Stage == TransitionStage.Preparing) await Broker(new { action = "abandon", id = t.Id }, ct);
            Save(state with { Transition = t }); return;
        }
        if (old.Stage == TransitionStage.Preparing)
        {
            if (!TransitionRules.ValidCommit(old, t, now)) throw new IOException("Invalid commit");
            await Broker(new { action = "commit", id = t.Id, switchAt = t.SwitchUtc!.Value.ToUnixTimeMilliseconds() / 1000.0,
                deadline = t.DeadlineUtc!.Value.ToUnixTimeMilliseconds() / 1000.0 }, ct);
            Save(state with { Transition = t });
        }
        else if (old.DeadlineUtc != t.DeadlineUtc || old.SwitchUtc != t.SwitchUtc) throw new IOException("Conflicting duplicate commit");
        if (now >= t.DeadlineUtc) return;
        await Receipt(t, "committed", ct);
        if (now < t.SwitchUtc) return;
        var local = await Broker(new { action = "status" }, ct);
        var journal = local.GetProperty("state").GetProperty("transition");
        if (journal.ValueKind == JsonValueKind.Null || local.GetProperty("active").GetString() != journal.GetProperty("target").GetString()) return;
        await Receipt(t, "reconnected", ct);
        if (t.Stage == TransitionStage.HubConfirmed)
        {
            await Broker(new { action = "finalize", id = t.Id }, ct);
            Save(state with { Transition = t });
            await Receipt(t, "finalized", ct);
        }
    }
    public async Task Tick(CancellationToken ct)
    {
        if (!await gate.WaitAsync(0, ct)) return;
        try
        {
            if (state.Candidate is not null)
            {
                try { await AdvanceCandidate(ct); }
                catch (Exception ex) when (ex is HttpRequestException or IOException or System.Net.Sockets.SocketException or OperationCanceledException)
                { Message = "Waiting for the authorized destination network and DM approval; previous binding remains saved."; }
                return;
            }
            if (state.Binding is not { } binding)
            {
                var now = time.GetUtcNow();
                if (!string.IsNullOrWhiteSpace(configuration["Satellite:PhoneCertificate"]) && (state.NextRecoveryUtc is null || now >= state.NextRecoveryUtc))
                {
                    Save(state with { NextRecoveryUtc = now.AddMinutes(15) });
                    await Broker(new { action = "open-provisioning" }, ct);
                    Show("http://127.0.0.1:5081/");
                }
                return;
            }
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(12));
                var status = await Poll(binding, timeout.Token);
                var wasOffline = state.Failures > 0;
                firstFailureTimestamp = null;
                Save(state with { UnavailableSince = null, Failures = 0, RecoveryAttempts = 0 });
                var brokerStatus = await Broker(new { action = "status" }, timeout.Token);
                var rootState = brokerStatus.GetProperty("state");
                var rootTransition = rootState.GetProperty("transition");
                if (rootTransition.ValueKind != JsonValueKind.Null && rootTransition.TryGetProperty("automatic", out var automatic) && automatic.GetBoolean() && rootTransition.GetProperty("phase").GetString() == "committed")
                    await Broker(new { action = "finalize", id = rootTransition.GetProperty("id").GetString() }, timeout.Token);
                if (state.Candidate is null && (!rootState.TryGetProperty("provisioning", out var lease) || lease.ValueKind == JsonValueKind.Null)) Show(binding.DisplayUrl, wasOffline);
                await Process(status, timeout.Token);
                Message = "Connected to paired hub";
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or OperationCanceledException or System.Net.Sockets.SocketException)
            {
                await Disconnect();
                var now = time.GetUtcNow();
                firstFailureTimestamp ??= time.GetTimestamp();
                Save(state with { UnavailableSince = state.UnavailableSince ?? now, Failures = state.Failures + 1 });
                Message = "Paired hub unavailable; association retained. Recovery is automatic; Setup requires owner authorization.";
                if (state.Failures >= 6 && time.GetElapsedTime(firstFailureTimestamp.Value) >= TimeSpan.FromSeconds(120) && (state.NextRecoveryUtc is null || now >= state.NextRecoveryUtc))
                {
                    // Commit cooldown before requesting a durable root-supervised attempt. Never spin after crashes.
                    Save(state with { NextRecoveryUtc = now.AddMinutes(Math.Min(15, 3 * Math.Pow(2, Math.Min(state.RecoveryAttempts, 3)))), RecoveryAttempts = state.RecoveryAttempts + 1 });
                    try { await Broker(new { action = "recover-profile" }, ct); } catch (IOException) { }
                    if (state.RecoveryAttempts >= 2 && !string.IsNullOrWhiteSpace(configuration["Satellite:PhoneCertificate"]))
                    {
                        try { await Broker(new { action = "open-provisioning" }, ct); } catch (IOException) { }
                    }
                    Show("http://127.0.0.1:5081/");
                }
            }
        }
        finally { gate.Release(); }
    }
}

public sealed class SatelliteWorker(SatelliteAgent agent, ILogger<SatelliteWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try { await agent.Tick(ct); }
            catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or InvalidOperationException)
            { logger.LogWarning("Satellite tick deferred ({FailureType})", ex.GetType().Name); }
        }
    }
}
