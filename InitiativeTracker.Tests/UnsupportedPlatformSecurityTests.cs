using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using InitiativeTracker.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;

namespace InitiativeTracker.Tests;

public sealed class UnsupportedPlatformSecurityTests
{
    [NonLinuxFact]
    public void EnabledBrowserTlsCannotStartOnUnsupportedPlatform()
    {
        var root = Path.Combine(Path.GetTempPath(), "unsupported-tls-" + Guid.NewGuid());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BrowserTls:Enabled"] = "true", ["BrowserTls:Host"] = "hub.owner-domain.org",
            ["BrowserTls:Store"] = Path.Combine(root, "certificates")
        }).Build();
        foreach (var ingress in new[] { "direct", "kernel443" })
        {
            config["BrowserTls:Ingress"] = ingress;
            Assert.Throws<InvalidOperationException>(() => BrowserTlsSettings.Read(config, Path.Combine(root, "app")));
        }
    }

    [NonLinuxFact]
    public void SocketCaptureCannotConferAuthorityOnUnsupportedPlatform()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        client.Connect(listener.LocalEndpoint);
        using var socket = listener.AcceptSocket();
        Assert.Null(BrowserIngress.Capture(socket, (_, _, _, _) => throw new InvalidOperationException("Must not probe non-Linux sockets.")));
    }

    [NonLinuxFact]
    public void SecurePersistenceCannotReadProtectedStateOrAcknowledgeWritesOnUnsupportedPlatform()
    {
        var root = Path.Combine(Path.GetTempPath(), "unsupported-grants-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var protection = new EphemeralDataProtectionProvider();
            var state = new SecureGrantState();
            var bytes = protection.CreateProtector("InitiativeTracker.SecureAccess.State.v1")
                .Protect(JsonSerializer.SerializeToUtf8Bytes(state));
            var path = Path.Combine(root, "state.bin"); File.WriteAllBytes(path, bytes);
            var store = new SecureGrantPersistence(new(root, true), protection);
            Assert.Null(store.Read());
            Assert.Throws<SecureAccessUnavailable>(() => store.Write(state));
            Assert.Throws<SecureAccessUnavailable>(() => store.Write(state, recovery: true));
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Null(new SecureGrantPersistence(new(root, true), protection).Read());
            Assert.Throws<PlatformNotSupportedException>(() => LinuxDirectoryDurability.Synchronize(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
