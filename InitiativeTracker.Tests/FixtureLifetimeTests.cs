using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeTracker.Tests;

public sealed class FixtureLifetimeTests
{
    private static TestApplicationFactory Application(string directory) => new(directory, builder => builder
        .UseEnvironment("Development").UseSetting("Tracker:DataDirectory", directory));

    private static void AssertWal(TestApplicationFactory app)
    {
        using var db = app.Services.GetRequiredService<IDbContextFactory<InitiativeTrackerDbContext>>().CreateDbContext();
        db.Database.OpenConnection();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        Assert.Equal("wal", command.ExecuteScalar());
    }

    private static void AssertReleased(string directory)
    {
        // Windows rejects this when any native SQLite handle is still open.
        using var exclusive = new FileStream(Path.Combine(directory, "initiative-tracker.db"),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.Length > 0);
    }

    [Fact]
    public async Task IndependentHostsReleaseFilesAndPreserveWalStateAcrossRepeatedRestarts()
    {
        var root = Path.Combine(Path.GetTempPath(), "fixture-lifetime-" + Guid.NewGuid());
        var firstDirectory = Path.Combine(root, "first");
        var otherDirectory = Path.Combine(root, "other");
        try
        {
            using var other = Application(otherDirectory);
            using var otherClient = other.CreateClient();
            Assert.True((await otherClient.GetAsync("/system/health")).IsSuccessStatusCode);
            other.Services.GetRequiredService<EncounterStateService>().AddCombatants("Independent encounter", 7, 12, 12, 1);
            for (var cycle = 0; cycle < 3; cycle++)
            {
                using (var app = Application(firstDirectory))
                {
                    using var client = app.CreateClient();
                    Assert.True((await client.GetAsync("/system/health")).IsSuccessStatusCode);
                    AssertWal(app);
                    var state = app.Services.GetRequiredService<EncounterStateService>();
                    Assert.Equal(cycle, state.CurrentEncounter.Combatants.Count);
                    state.AddCombatants("Persisted cycle " + cycle, cycle + 1, 20, 20, 1);
                }
                AssertReleased(firstDirectory);
                // Clearing the first pool must leave the concurrent host usable.
                Assert.True((await otherClient.GetAsync("/system/health")).IsSuccessStatusCode);
                Assert.Equal("Independent encounter", Assert.Single(other.Services.GetRequiredService<EncounterStateService>()
                    .CurrentEncounter.Combatants).Name);
            }
            using (var restarted = Application(firstDirectory))
            {
                using var client = restarted.CreateClient();
                Assert.True((await client.GetAsync("/system/health")).IsSuccessStatusCode);
                var combatants = restarted.Services.GetRequiredService<EncounterStateService>().CurrentEncounter.Combatants;
                Assert.Equal(3, combatants.Count);
                Assert.Equal(new[] { "Persisted cycle 0", "Persisted cycle 1", "Persisted cycle 2" }, combatants.Select(c => c.Name).Order());
                AssertWal(restarted);
            }
            AssertReleased(firstDirectory);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        Assert.False(Directory.Exists(root));
    }
}
