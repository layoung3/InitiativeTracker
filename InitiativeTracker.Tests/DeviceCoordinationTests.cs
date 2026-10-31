using System.Security.Cryptography.X509Certificates;
using InitiativeTracker.Core.Coordination;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using InitiativeTracker.Web.Data;

namespace InitiativeTracker.Tests;

public sealed class DeviceCoordinationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "n8-tests-" + Guid.NewGuid());
    private readonly WebApplicationFactory<EncounterStateService> app;
    private readonly FakeNetwork network = new();
    private DeviceCoordinationService Service => app.Services.GetRequiredService<DeviceCoordinationService>();
    private readonly DateTimeOffset now = DateTimeOffset.UtcNow;
    public DeviceCoordinationTests()
    {
        app = new WebApplicationFactory<EncounterStateService>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development").UseSetting("Tracker:DataDirectory", directory).UseSetting("Coordination:Enabled", "true");
            builder.ConfigureServices(services => { services.RemoveAll<ICoordinationNetwork>(); services.AddSingleton<ICoordinationNetwork>(network); });
        });
    }
    private (Enrollment Enrollment, X509Certificate2 Certificate) Enroll(bool approve = true)
    {
        var cert = DeviceProtocol.Certificate(Path.Combine(directory, Guid.NewGuid() + ".pfx"), "Synthetic satellite", false);
        var enrollment = Service.Request(cert, new(Guid.NewGuid(), "Synthetic satellite", 1, DeviceProtocol.Capabilities), now);
        if (approve) Service.Approve(enrollment.DeviceId, DeviceProtocol.Fingerprint(cert), now);
        return (enrollment, cert);
    }
    private DeviceReceipt Receipt(Enrollment enrollment, Transition t, string stage) => new(1, t.Id, t.Revision, enrollment.Generation, stage);
    private async Task<Transition> Prepare() { await Service.PrepareAsync("wifi", Guid.NewGuid().ToString(), default); return Service.Snapshot.Transition!; }
    [Fact]
    public void CertificateTrustIsIndependentFromDisplayCookiesAndRequiresPhysicalPin()
    {
        var (enrollment, cert) = Enroll(false);
        using (cert)
        {
            Assert.Throws<UnauthorizedAccessException>(() => Service.Status(cert));
            Assert.Throws<InvalidOperationException>(() => Service.Approve(enrollment.DeviceId, new string('0', 64), now));
            Service.Approve(enrollment.DeviceId, DeviceProtocol.Fingerprint(cert), now);
            Assert.Equal(enrollment.Generation, Service.Status(cert).Generation);
            Service.Revoke(enrollment.DeviceId);
            Assert.Throws<UnauthorizedAccessException>(() => Service.Status(cert));
        }
    }
    [Fact]
    public async Task UnreadyAndOfflineDevicesRequireExplicitLeaveBehindChoice()
    {
        var (device, cert) = Enroll(); using var certificate = cert;
        var t = await Prepare();
        Assert.Throws<InvalidOperationException>(() => Service.Commit(t.Id, [], now));
        Assert.Throws<InvalidOperationException>(() => Service.Commit(t.Id, [Guid.NewGuid()], now));
        Service.Commit(t.Id, [device.DeviceId], now);
        Assert.Contains(device.DeviceId, Service.Snapshot.Transition!.LeftBehind);
        Assert.Throws<InvalidOperationException>(() => Service.Postpone(t.Id));
        Assert.Null(Service.Status(cert).Plan);
    }
    [Fact]
    public async Task ReadyCommitAndReconnectionAreDistinctFromHubConfirmation()
    {
        var (device, cert) = Enroll(); using var certificate = cert;
        var t = await Prepare(); var plan = Service.Status(cert).Plan!;
        Assert.Equal("synthetic-destination", plan.Target.Ssid);
        Service.Receipt(cert, Receipt(device, t, "ready"), now);
        Service.Receipt(cert, Receipt(device, t, "ready"), now); // Duplicate readiness is idempotent.
        Service.Commit(t.Id, [], now);
        var committed = Service.Snapshot.Transition!;
        Assert.Throws<InvalidOperationException>(() => Service.Commit(t.Id, [], now.AddSeconds(1)));
        Assert.Equal(now.AddSeconds(190), committed.DeadlineUtc);
        Assert.Throws<InvalidOperationException>(() => Service.Receipt(cert, Receipt(device, t, "reconnected"), now.AddSeconds(11)));
        Service.Receipt(cert, Receipt(device, t, "committed"), now);
        Service.Receipt(cert, Receipt(device, t, "reconnected"), now.AddSeconds(11));
        Assert.True(Service.Snapshot.Transition!.Devices[0].Reconnected);
        Assert.Equal(TransitionStage.Committed, Service.Snapshot.Transition.Stage);
        Assert.Throws<InvalidOperationException>(() => Service.Receipt(cert, Receipt(device, t, "finalized"), now.AddSeconds(12)));
    }
    [Fact]
    public async Task RevocationImmediatelyInvalidatesPreparedReadinessAndReenrollmentUsesNewGeneration()
    {
        var (device, cert) = Enroll(); using var certificate = cert;
        var t = await Prepare(); Service.Status(cert); Service.Receipt(cert, Receipt(device,t,"ready"),now);
        Service.Revoke(device.DeviceId);
        Assert.Throws<UnauthorizedAccessException>(()=>Service.Status(cert));
        Assert.False(Service.Snapshot.Transition!.Devices[0].Ready);
        var renewed=Service.Request(cert,new(device.DeviceId,"Synthetic satellite",1,DeviceProtocol.Capabilities),now);
        Assert.NotEqual(device.Generation,renewed.Generation);
        Service.Approve(renewed.DeviceId,renewed.CertificatePin,now); Service.Status(cert);
        Assert.Throws<InvalidOperationException>(()=>Service.Commit(t.Id,[],now));
        Assert.Throws<InvalidOperationException>(()=>Service.Receipt(cert,Receipt(device,t,"ready"),now));
        Service.Commit(t.Id,[device.DeviceId],now);
    }
    private Guid LegacyDisplay()
    {
        using var db=app.Services.GetRequiredService<IDbContextFactory<InitiativeTrackerDbContext>>().CreateDbContext();
        var display=new RegisteredDevice {Name="Existing paired display",IsTrusted=true,DeviceType="Display",PairedAtUtc=DateTime.UtcNow};
        db.RegisteredDevices.Add(display); db.SaveChanges(); return display.Id;
    }
    [Fact]
    public async Task ExistingDisplayWithoutAgentRequiresExplicitLeaveBehindChoice()
    {
        var display=LegacyDisplay(); var t=await Prepare();
        var legacy=Assert.Single(t.Devices); Assert.Equal(display,legacy.DeviceId); Assert.False(legacy.Ready);
        Assert.Equal("Existing paired display",legacy.Name);
        Assert.Throws<InvalidOperationException>(()=>Service.Commit(t.Id,[],now));
        Service.Commit(t.Id,[display],now); Assert.Contains(display,Service.Snapshot.Transition!.LeftBehind);
    }
    [Fact]
    public async Task ExplicitPhysicalInventoryLinkPreservesDisplayPairingWithoutPromotingItsCookie()
    {
        var display=LegacyDisplay(); var (device,cert)=Enroll(); using var certificate=cert;
        Service.Approve(device.DeviceId,device.CertificatePin,now,display);
        var t=await Prepare(); Assert.Equal(device.DeviceId,Assert.Single(t.Devices).DeviceId);
        Service.Status(cert); Service.Receipt(cert,Receipt(device,t,"ready"),now);
        Service.Commit(t.Id,[],now);
        Assert.True(app.Services.GetRequiredService<DeviceRegistryService>().IsTrustedDisplay(display));
    }
    [Fact]
    public async Task DisplayPairedAfterPreparationCannotBeSilentlyOmittedAtCommit()
    {
        var t=await Prepare(); LegacyDisplay();
        Assert.Throws<InvalidOperationException>(()=>Service.Commit(t.Id,[],now));
    }
    [Fact]
    public async Task RestartPreservesDeadlineRevisionAndProtectedProfiles()
    {
        var (device, cert) = Enroll(); using var certificate = cert;
        var t = await Prepare(); Service.Status(cert); Service.Receipt(cert, Receipt(device, t, "ready"), now); Service.Commit(t.Id, [], now);
        var expected = Service.Snapshot;
        var restored = ActivatorUtilities.CreateInstance<DeviceCoordinationService>(app.Services);
        Assert.Equal(expected.Transition!.DeadlineUtc, restored.Snapshot.Transition!.DeadlineUtc);
        Assert.Equal(t.Revision, restored.Snapshot.Revision);
        Assert.Equal("synthetic-secret", restored.Status(cert).Plan!.Target.Password);
        var raw = File.ReadAllText(Path.Combine(directory, "coordination", "hub.json"));
        Assert.DoesNotContain("synthetic-secret", raw);
        if (OperatingSystem.IsLinux()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(directory, "coordination", "hub.json")));
    }
    [Fact]
    public async Task ReplayGenerationMismatchWrongDeviceAndLateMessagesAreRejected()
    {
        var (device, cert) = Enroll(); using var certificate = cert;
        var t = await Prepare();
        Assert.Throws<InvalidOperationException>(() => Service.Receipt(cert, Receipt(device, t, "ready") with { Generation = Guid.NewGuid() }, now));
        Assert.Throws<InvalidOperationException>(() => Service.Receipt(cert, Receipt(device, t, "ready") with { Revision = t.Revision-1 }, now));
        Assert.Throws<InvalidOperationException>(() => Service.Receipt(cert, Receipt(device, t, "ready"), t.PrepareExpiresUtc));
        Assert.Throws<InvalidOperationException>(() => Service.Commit(t.Id, [], t.PrepareExpiresUtc));
        Service.Postpone(t.Id);
        var next = await Prepare(); Assert.True(next.Revision > t.Revision);
        Assert.Throws<InvalidOperationException>(() => Service.Receipt(cert, Receipt(device, t, "ready"), now));
    }
    [Fact]
    public void EnrollmentIsBoundedExpiringAndConflictingDeviceIdsCannotReplaceKeys()
    {
        var (device, cert) = Enroll(false); using var certificate = cert;
        Assert.Throws<InvalidOperationException>(() => Service.Approve(device.DeviceId, device.CertificatePin, now.AddMinutes(6)));
        var renewed = Service.Request(cert, new(device.DeviceId, "Synthetic satellite", 1, DeviceProtocol.Capabilities), now.AddMinutes(6));
        Assert.NotEqual(device.Generation, renewed.Generation);
        using var attacker = DeviceProtocol.Certificate(Path.Combine(directory, "attacker.pfx"), "Synthetic unrelated", false);
        Assert.Throws<InvalidOperationException>(() => Service.Request(attacker, new(device.DeviceId, "Impostor", 1, DeviceProtocol.Capabilities), now));
    }
    [Fact]
    public async Task HttpCannotFetchPlansSubmitCredentialsOrPromoteDisplayIdentity()
    {
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await client.GetAsync("/api/coordination/status")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await client.PostAsync("/dm/network?handler=Provision", new FormUrlEncodedContent(new Dictionary<string,string> { ["WifiPassword"] = "synthetic-secret" }))).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await client.PostAsync("/dm/network?handler=RotateHotspotPassword", new StringContent(""))).StatusCode);
        Assert.Equal(0, network.Schedules);
    }
    [Fact]
    public void ProtocolRejectsWrongHubDeviceVersionStalePlanAndExtendedCommit()
    {
        var b = new DeviceBinding(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('A', 64), "https://synthetic.invalid", "http://synthetic.invalid/display");
        var t = new Transition(Guid.NewGuid(), 1, b.HubId, "wifi", Guid.NewGuid().ToString(), now.AddMinutes(2), TransitionStage.Preparing, null, null, [new(b.DeviceId,b.Generation)], []);
        var p = new TransitionPlan(1,t,b.DeviceId,b.Generation,new("synthetic", "synthetic-pass"),new("synthetic-recovery","synthetic-pass"));
        Assert.True(TransitionRules.ValidPlan(p,b,0,now));
        Assert.False(TransitionRules.ValidPlan(p with { Version = 2 },b,0,now));
        Assert.False(TransitionRules.ValidPlan(p,b with { HubId = Guid.NewGuid() },0,now));
        Assert.False(TransitionRules.ValidPlan(p,b,1,now));
        var commit = t with { Stage = TransitionStage.Committed, SwitchUtc = now.AddSeconds(10), DeadlineUtc = now.AddSeconds(190) };
        Assert.True(TransitionRules.ValidCommit(t, commit, now));
        Assert.False(TransitionRules.ValidCommit(t, commit with { DeadlineUtc = now.AddHours(1) }, now));
        Assert.False(TransitionRules.ValidCommit(t, commit with { TargetUuid = Guid.NewGuid().ToString() }, now));
    }
    [Fact]
    public void BrowserTlsFailsClosedWithoutPublicTrustAndNeverUsesTheAgentPinAsBrowserTrust()
    {
        Assert.False(new PublicCertificateHttps(null,null).Ready);
        var path = Path.Combine(directory, "self-signed.pfx");
        using var cert = DeviceProtocol.Certificate(path,"synthetic.invalid",true);
        Assert.False(new PublicCertificateHttps("synthetic.invalid",path).Ready);
        Assert.True(DeviceProtocol.PinMatches(cert,DeviceProtocol.Fingerprint(cert)));
        Assert.False(DeviceProtocol.PinMatches(cert,new string('0',64)));
    }
    public void Dispose() { app.Dispose(); if (Directory.Exists(directory)) Directory.Delete(directory,true); }
    private sealed class FakeNetwork : ICoordinationNetwork
    {
        public int Schedules;
        public Task<(WifiProfile Target, WifiProfile Recovery)> ProfilesAsync(string kind,string? uuid,CancellationToken ct) =>
            Task.FromResult((new WifiProfile("synthetic-destination","synthetic-secret"),new WifiProfile("synthetic-recovery","synthetic-secret")));
        public Task<bool> ScheduleAsync(Transition t,CancellationToken ct) { Schedules++; return Task.FromResult(true); }
        public Task<bool> ConfirmAsync(Transition t,CancellationToken ct) => Task.FromResult(true);
    }
}
