using InitiativeTracker.Core.Networking;
using Xunit;

namespace InitiativeTracker.Tests;

public class WifiProvisioningValidationTests
{
    [Theory]
    [InlineData("Home", "password123", true)]
    [InlineData("Cafe", null, false)]
    public void ValidEntryPasses(string ssid, string? password, bool secured) =>
        Assert.Empty(WifiProvisioningValidation.Validate(ssid, password, secured));

    [Theory]
    [InlineData(null, "password123", true)]
    [InlineData("", "password123", true)]
    [InlineData("Home", "short", true)]
    [InlineData("Home", "bad password\n", true)]
    [InlineData("Cafe", "notallowed", false)]
    public void InvalidEntryIsRejected(string? ssid, string? password, bool secured) =>
        Assert.NotEmpty(WifiProvisioningValidation.Validate(ssid, password, secured));

    [Fact]
    public void OversizeUtf8SsidIsRejected() =>
        Assert.NotEmpty(WifiProvisioningValidation.Validate(new string('é', 20), "password123", true));
}
