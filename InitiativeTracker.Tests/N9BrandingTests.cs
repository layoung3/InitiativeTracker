using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace InitiativeTracker.Tests;

public sealed class N9BrandingTests
{
    [Fact]
    public async Task SuppliedOriginalLogoIsServedUnchanged()
    {
        var directory = Path.Combine(Path.GetTempPath(), "n9-logo-" + Guid.NewGuid());
        try
        {
            using var app = new TestApplicationFactory(directory, builder => builder
                .UseEnvironment("Development").UseSetting("Tracker:DataDirectory", directory));
            using var client = app.CreateClient();
            var bytes = await client.GetByteArrayAsync("/images/colophon.png");
            Assert.Equal(73110, bytes.Length);
            Assert.Equal("17822fd1b2b7f54f3b084ce19b24d51f50f30e0495529ba8679586145def40b1",
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
            var html = await client.GetStringAsync("/join");
            Assert.Contains("src=\"/branding/logo\"", html);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

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
            using var app = new TestApplicationFactory(directory, builder => builder
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
            using var app = new TestApplicationFactory(directory, builder => builder
                .UseEnvironment("Development").UseSetting("Tracker:DataDirectory", directory)
                .UseSetting("Branding:ShowApplicationLogo", "false"));
            using var client = app.CreateClient();
            var html = await client.GetStringAsync("/join");
            Assert.Matches("data-optional-brand[^>]*hidden", html);
            Assert.Contains("Join Game", html);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
