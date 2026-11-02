using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace InitiativeTracker.Tests;

// Keep production SQLite pooling and WAL enabled. A stopped test host owns the
// pool for its unique temporary database; disposing DbContexts returns handles
// to that pool rather than releasing the files on Windows.
internal sealed class TestApplicationFactory(string directory, Action<IWebHostBuilder> configure)
    : WebApplicationFactory<EncounterStateService>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => configure(builder);

    public override async ValueTask DisposeAsync()
    {
        // Disposes clients and derived factories, then awaits hosted services
        // before disposing the host/service provider. Caller-owned contexts and
        // sockets must have left their using scopes before this point.
        await base.DisposeAsync();
        using var connection = new SqliteConnection($"Data Source={Path.Combine(directory, "initiative-tracker.db")}");
        SqliteConnection.ClearPool(connection); // Never clear another fixture's pool.
    }
}
