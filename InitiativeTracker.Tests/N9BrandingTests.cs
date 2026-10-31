using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace InitiativeTracker.Tests;

public sealed class N9BrandingTests
{
    [Theory]
    [InlineData(null, "Initiative Tracker")]
    [InlineData("  Table of Stars  ", "Table of Stars")]
    [InlineData("", "Initiative Tracker")]
    [InlineData("bad\nname", "Initiative Tracker")]
    public void NamesAreBounded(string? value, string expected) =>
        Assert.Equal(expected, BrandingService.NormalizeName(value));

    [Fact]
    public async Task ConfiguredNameIsEncodedInActualPages()
    {
        var directory = Path.Combine(Path.GetTempPath(), "n9-branding-" + Guid.NewGuid());
        try
        {
            using var app = new WebApplicationFactory<EncounterStateService>().WithWebHostBuilder(builder => builder
                .UseEnvironment("Development").UseSetting("Tracker:DataDirectory", directory)
                .UseSetting("Branding:ApplicationName", "<script>alert('name')</script>"));
            using var client = app.CreateClient();
            var html = await client.GetStringAsync("/join");
            Assert.Contains("&lt;script&gt;", html);
            Assert.DoesNotContain("<script>alert('name')</script>", html);
            Assert.DoesNotContain("/etc/passwd", html);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task OptionalBrandingCanBeDisabledOnApplicationPages()
    {
        var directory = Path.Combine(Path.GetTempPath(), "n9-branding-" + Guid.NewGuid());
        try
        {
            using var app = new WebApplicationFactory<EncounterStateService>().WithWebHostBuilder(builder => builder
                .UseEnvironment("Development").UseSetting("Tracker:DataDirectory", directory)
                .UseSetting("Branding:ShowApplicationLogo", "false"));
            using var client = app.CreateClient();
            var html = await client.GetStringAsync("/join");
            Assert.DoesNotContain("tracker-brand-name", html);
            Assert.Contains("Join Game", html);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
