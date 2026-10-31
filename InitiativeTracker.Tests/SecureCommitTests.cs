using System.Collections.Concurrent;
using System.Diagnostics;
using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using static InitiativeTracker.Tests.SecureAccessWebTests;

namespace InitiativeTracker.Tests;

public sealed class SecureCommitTests
{
    private static int Commit(Host host, SecureGrant grant, string marker) => host.Access.CommitAuthorized(grant,
        grant.Browser, grant.Origin, SecureAccessService.Principal(grant.Identity, CookieAuthenticationDefaults.AuthenticationScheme), () => SaveMarker(host, marker));
    private static int SaveMarker(Host host, string name)
    {
        using var db = host.Factory.CreateDbContext();
        db.Database.SetCommandTimeout(1);
        db.PlayerProfiles.Add(new PlayerProfile { Name = name, CreatedAtUtc = DateTime.UtcNow });
        return db.SaveChanges();
    }
    private static bool HasMarker(Host host, string name)
    {
        using var db = host.Factory.CreateDbContext();
        return db.PlayerProfiles.Any(p => p.Name == name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevocationOrCancellationFirstPreventsDatabaseCommit(bool cancel)
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var grant = host.Access.Grants().Single();
        Assert.Equal(System.Net.HttpStatusCode.Redirect, cancel
            ? (await dm.Post("Cancel")).StatusCode : (await dm.Post("Revoke", ("id", grant.Id.ToString()))).StatusCode);
        Assert.Throws<SecureAccessDenied>(() => Commit(host, grant, "must not commit"));
        Assert.False(HasMarker(host, "must not commit"));
        host.Delete();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitFirstExcludesSuccessfulRevokeOrCancelUntilAfterDatabaseCommit(bool cancel)
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var grant = host.Access.Grants().Single();
        // Synthetic barriers force ordering only; production callbacks are bounded database commits.
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var revoking = new ManualResetEventSlim(); var order = new ConcurrentQueue<string>();
        var writer = Task.Run(() => host.Access.CommitAuthorized(grant, grant.Browser, grant.Origin,
            SecureAccessService.Principal(grant.Identity, CookieAuthenticationDefaults.AuthenticationScheme), () =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Fixture commit release timed out");
                var rows = SaveMarker(host, "committed before revoke"); order.Enqueue("commit"); return rows;
            }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Task revoke = Task.CompletedTask;
        try
        {
            revoke = Task.Run(() =>
            {
                revoking.Set();
                if (cancel) host.Access.Cancel(grant.Browser); else host.Access.Revoke(grant, grant.Id);
                order.Enqueue("revoke");
            });
            Assert.True(revoking.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(revoke.IsCompleted);
            Assert.False(HasMarker(host, "committed before revoke"));
        }
        finally { release.Set(); }
        await Task.WhenAll(writer, revoke).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "commit", "revoke" }, order.ToArray());
        Assert.True(HasMarker(host, "committed before revoke"));
        Assert.Throws<SecureAccessDenied>(() => Commit(host, grant, "after revoke"));
        host.Delete();
    }

    [Fact]
    public async Task ExpiryIsCheckedAtCommitEntryAndDoesNotExtendTheGrant()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var grant = host.Access.Grants().Single();
        host.Clock.Advance(TimeSpan.FromHours(12) - TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, host.Access.CommitAuthorized(grant, grant.Browser, grant.Origin,
            SecureAccessService.Principal(grant.Identity, CookieAuthenticationDefaults.AuthenticationScheme), () =>
            {
                host.Clock.Advance(TimeSpan.FromSeconds(1));
                return SaveMarker(host, "admitted before expiry");
            }));
        Assert.True(HasMarker(host, "admitted before expiry"));
        Assert.Throws<SecureAccessDenied>(() => Commit(host, grant, "expired"));
        Assert.False(HasMarker(host, "expired")); host.Delete();
    }

    [Fact]
    public async Task ContendedGateTimesOutWithoutInvokingOrAbandoningTheSecondCommit()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var grant = host.Access.Grants().Single();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var writer = Task.Run(() => host.Access.CommitAuthorized(grant, grant.Browser, grant.Origin,
            SecureAccessService.Principal(grant.Identity, CookieAuthenticationDefaults.AuthenticationScheme), () =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Fixture commit release timed out");
                return SaveMarker(host, "first writer");
            }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var called = false;
        try
        {
            var watch = Stopwatch.StartNew();
            Assert.Throws<SecureAccessUnavailable>(() => host.Access.CommitAuthorized(grant, grant.Browser, grant.Origin,
                SecureAccessService.Principal(grant.Identity, CookieAuthenticationDefaults.AuthenticationScheme), () => { called = true; return SaveMarker(host, "timed out writer"); }));
            Assert.InRange(watch.Elapsed.TotalMilliseconds, 150, 3000);
            Assert.False(called);
        }
        finally { release.Set(); }
        await writer.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(HasMarker(host, "timed out writer"));
        Assert.Equal(1, Commit(host, grant, "later writer")); host.Delete();
    }

    [Fact]
    public async Task CommitExceptionPropagatesAndAlwaysReleasesTheGate()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var grant = host.Access.Grants().Single(); var failure = new InvalidOperationException("Fixture transaction failure");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => host.Access.CommitAuthorized(grant, grant.Browser, grant.Origin,
            SecureAccessService.Principal(grant.Identity, CookieAuthenticationDefaults.AuthenticationScheme), () => throw failure)));
        Assert.Equal(1, Commit(host, grant, "after exception"));
        host.Access.Revoke(grant, grant.Id);
        Assert.Throws<SecureAccessDenied>(() => Commit(host, grant, "revoked after exception")); host.Delete();
    }

    [Theory]
    [InlineData("browser")]
    [InlineData("origin")]
    [InlineData("principal")]
    [InlineData("expected")]
    public async Task BindingMixedPrincipalAndAlteredExpectedGrantNeverReachCommit(string mismatch)
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap();
        var grant = host.Access.Grants().Single(); var expected = mismatch == "expected" ? grant with { Generation = grant.Generation + 1 } : grant;
        var legacy = SecureAccessService.Principal(mismatch == "principal" ? grant.Identity with { Role = "Display" } : grant.Identity, CookieAuthenticationDefaults.AuthenticationScheme);
        Assert.Throws<SecureAccessDenied>(() => host.Access.CommitAuthorized(expected,
            mismatch == "browser" ? "different-browser" : grant.Browser,
            mismatch == "origin" ? "https://other.owner-domain.org:5445" : grant.Origin, legacy, () => SaveMarker(host, "bad binding")));
        Assert.False(HasMarker(host, "bad binding")); host.Delete();
    }

    [Fact]
    public async Task DisplayGrantAndCorruptStateCannotAuthorizeWrites()
    {
        using var host = new Host(); using var dm = host.Client(); await dm.Bootstrap(); Guid id;
        using (var db = host.Factory.CreateDbContext()) { var display = new RegisteredDevice { Name = "Read only", PairedAtUtc = DateTime.UtcNow }; db.Add(display); db.SaveChanges(); id = display.Id; }
        using var screen = host.Client(); screen.Legacy("Display", display: id); await Enroll(screen, dm);
        var displayGrant = host.Access.Grants().Single(g => g.Identity.Role == "Display");
        Assert.Throws<SecureAccessDenied>(() => Commit(host, displayGrant, "display write"));
        var grant = host.Access.Grants().Single(g => g.Identity.Role == "DM");
        File.WriteAllBytes(Path.Combine(host.DirectoryPath, "secure-access", "state.bin"), [0, 1, 2]);
        Assert.Throws<SecureAccessDenied>(() => Commit(host, grant, "corrupt state"));
        Assert.False(HasMarker(host, "display write")); Assert.False(HasMarker(host, "corrupt state")); host.Delete();
    }

    [Fact]
    public async Task CommitReadsPersistentGrantAndRevocationAcrossRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "secure-commit-restart-" + Guid.NewGuid()); SecureGrant grant;
        using (var first = new Host(directory)) { using var dm = first.Client(); await dm.Bootstrap(); grant = first.Access.Grants().Single(); }
        using (var second = new Host(directory)) { Assert.Equal(1, Commit(second, grant, "after restart")); second.Access.Revoke(grant, grant.Id); }
        using (var third = new Host(directory)) { Assert.Throws<SecureAccessDenied>(() => Commit(third, grant, "revoked restart")); Assert.False(HasMarker(third, "revoked restart")); }
        Directory.Delete(directory, true);
    }
}
