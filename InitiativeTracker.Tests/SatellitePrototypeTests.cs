using InitiativeTracker.Satellite;
using InitiativeTracker.Core.Coordination;
using Microsoft.Extensions.Configuration;

namespace InitiativeTracker.Tests;

public sealed class SatellitePrototypeTests
{
    [Fact]
    public void FirstBootCreatesUniqueOwnerAndIndependentIdentityWithoutClaimingHub()
    {
        var directory = Path.Combine(Path.GetTempPath(),"n8-agent-"+Guid.NewGuid());
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            { ["Satellite:StateDirectory"] = directory, ["Satellite:KioskState"] = Path.Combine(directory,"kiosk.json") }).Build();
            var first = new SatelliteAgent(config);
            Assert.Null(first.Snapshot.Binding);
            Assert.NotNull(first.SetupOwnerCredential);
            Assert.True(first.Owner(first.SetupOwnerCredential));
            Assert.False(first.Owner(new string('0',64)));
            Assert.False(first.Owner(null));
            var second = new SatelliteAgent(config);
            Assert.Equal(first.Snapshot.DeviceId,second.Snapshot.DeviceId);
            Assert.Equal(first.Pin,second.Pin);
            Assert.Null(second.SetupOwnerCredential);
            first.Show("http://synthetic.invalid/display");
            var kiosk = File.ReadAllText(Path.Combine(directory,"kiosk.json"));
            Assert.DoesNotContain(first.SetupOwnerCredential!,kiosk);
            first.Show("http://synthetic.invalid/display");
            Assert.Equal(kiosk,File.ReadAllText(Path.Combine(directory,"kiosk.json")));
            if (OperatingSystem.IsLinux()) Assert.Equal(UnixFileMode.UserRead|UnixFileMode.UserWrite,File.GetUnixFileMode(Path.Combine(directory,"agent.pfx")));
        }
        finally { Directory.Delete(directory,true); }
    }
}
