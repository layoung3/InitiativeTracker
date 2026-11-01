using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using InitiativeTracker.Web.Infrastructure;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace InitiativeTracker.Tests;

public sealed class BrowserIngressTests
{
    private const string Host = "hub-fixture.colophon.stream";
    private static byte[] Address(string ip, int port)
    {
        var address = IPAddress.Parse(ip);var ipv4 = address.AddressFamily == AddressFamily.InterNetwork;
        var value = new byte[ipv4 ? 16 : 28];
        BitConverter.TryWriteBytes(value.AsSpan(), (ushort)(ipv4 ? 2 : 10));
        BinaryPrimitives.WriteUInt16BigEndian(value.AsSpan(2), (ushort)port);
        address.GetAddressBytes().CopyTo(value, ipv4 ? 4 : 8);return value;
    }
    private static BrowserIngress Capture(Socket accepted, byte[] value) => BrowserIngress.Capture(accepted, (_,level,option,buffer) =>
    {
        Assert.Equal(0,level);Assert.Equal(80,option);value.CopyTo(buffer,0);return value.Length;
    })!;
    private static void Accepted(Action<Socket> test)
    {
        using var listener = new TcpListener(IPAddress.Loopback,5445);listener.Start();
        using var client = new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp);
        client.Connect(listener.LocalEndpoint);using var accepted = listener.AcceptSocket();test(accepted);
    }
    private static DefaultHttpContext Context(BrowserIngress? ingress = null)
    {
        var context = new DefaultHttpContext();context.Request.Scheme = "https";context.Request.Host = new(Host);
        context.Request.Path = "/system/health";context.Request.Method = "GET";
        context.Connection.LocalPort = 5445;context.Connection.LocalIpAddress = context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Features.Set<ITlsHandshakeFeature>(new Handshake());
        if (ingress is not null) context.Features.Set(ingress);return context;
    }
    #pragma warning disable SYSLIB0058 // Required legacy members of the framework handshake feature interface.
    private sealed class Handshake : ITlsHandshakeFeature
    {
        public SslProtocols Protocol => SslProtocols.Tls13;
        public CipherAlgorithmType CipherAlgorithm => CipherAlgorithmType.Aes256;
        public int CipherStrength => 256;
        public HashAlgorithmType HashAlgorithm => HashAlgorithmType.Sha256;
        public int HashStrength => 256;
        public ExchangeAlgorithmType KeyExchangeAlgorithm => ExchangeAlgorithmType.DiffieHellman;
        public int KeyExchangeStrength => 256;
    }
    #pragma warning restore SYSLIB0058
    private static BrowserCertificateStore Store() => new(new(true,Host,5445,"/tmp/not-a-runtime-cert-store",443,"kernel443"),NullLogger<BrowserCertificateStore>.Instance);

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.7.3")]
    [InlineData("::1")]
    [InlineData("fd12::5")]
    public void OriginalAddressRequiresExactFamilyLengthAndPrivateScope(string ip)
    {
        var family = IPAddress.Parse(ip).AddressFamily;var data = Address(ip,443);
        Assert.Equal(new IPEndPoint(IPAddress.Parse(ip),443),BrowserIngress.ParseOriginal(data,family));
        Assert.Null(BrowserIngress.ParseOriginal(data[..^1],family));
        Assert.Null(BrowserIngress.ParseOriginal([..data,0],family));
        Assert.Null(BrowserIngress.ParseOriginal(data,family == AddressFamily.InterNetwork ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork));
        data[2] = data[3] = 0;Assert.Null(BrowserIngress.ParseOriginal(data,family));
    }
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("169.254.1.1")]
    [InlineData("2001:db8::1")]
    [InlineData("::ffff:127.0.0.1")]
    public void AmbiguousOrNonprivateDestinationsCannotBecomeAuthority(string ip) =>
        Assert.Null(BrowserIngress.ParseOriginal(Address(ip,443),IPAddress.Parse(ip).AddressFamily));

    [Fact]
    public void MissingMetadataForwardedHeadersAndDirectBackendCannotConferTrust()
    {
        using var store = Store();var context = Context();
        context.Request.Headers["X-Forwarded-Proto"] = "https";context.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
        context.Request.Headers["X-Forwarded-Host"] = Host;
        Assert.False(store.MatchesCanonicalTransport(context));Assert.False(store.Physical(context));
        Accepted(socket =>
        {
            foreach (var bytes in new[] { Array.Empty<byte>(), Address("127.0.0.1",5445), Address("192.168.1.1",443) })
            {
                var ingress = Capture(socket,bytes);context.Features.Set(ingress);
                Assert.False(ingress.Canonical);Assert.False(ingress.Physical);Assert.False(store.MatchesCanonicalTransport(context));
            }
        });
    }
    [Fact]
    public void ProvenanceIsCapturedOnceAndMustMatchActualConnectionAndAuthority()
    {
        using var store = Store();Assert.Equal("https://"+Host,store.Origin);
        Accepted(socket =>
        {
            var bytes = Address("127.0.0.1",443);var ingress = Capture(socket,bytes);Array.Clear(bytes);
            Assert.True(ingress.Canonical);Assert.True(ingress.Physical);
            var context = Context(ingress);Assert.True(store.MatchesCanonicalTransport(context));
            context.Request.Host = new(Host,5445);Assert.False(store.MatchesCanonicalTransport(context));
            context.Request.Host = new("other.colophon.stream");Assert.False(store.MatchesCanonicalTransport(context));
            context.Request.Host = new(Host);context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.2");
            Assert.False(store.MatchesCanonicalTransport(context));
            context.Connection.RemoteIpAddress = IPAddress.Loopback;context.Request.Scheme = "http";
            Assert.False(store.MatchesCanonicalTransport(context));
        });
    }
    [Fact]
    public void BackendProbeExceptionIsOnlyExactReadOnlyStatusAndNeverGeneralAuthority()
    {
        using var store = Store();Accepted(socket =>
        {
            var ingress = Capture(socket,Address("127.0.0.1",5445));var context = Context(ingress);
            foreach (var path in new[] { "/secure-access/physical","/media","/appearance","/system/https-status/","/SYSTEM/https-status","/system/health" })
            { context.Request.Path = path;Assert.False(store.MatchesStatusProbeTransport(context)); }
            context.Request.Path = "/system/https-status";
            foreach (var method in new[] { "GET","HEAD" })
            { context.Request.Method = method;Assert.True(store.MatchesStatusProbeTransport(context));Assert.False(store.MatchesCanonicalTransport(context));Assert.False(ingress.Physical); }
            context.Request.Method = "POST";Assert.False(store.MatchesStatusProbeTransport(context));
            context.Request.Method = "GET";context.Request.PathBase = new("/media");Assert.False(store.MatchesStatusProbeTransport(context));context.Request.PathBase = new();
            context.Request.Method = "GET";context.Request.QueryString = new("?handler=anything");Assert.False(store.MatchesStatusProbeTransport(context));
        });
    }
    [Fact]
    public void ProfilesRejectAmbiguousPublicPortsAndKeepLegacyDirectMode()
    {
        IConfiguration Values(string ingress,string port) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        { ["BrowserTls:Enabled"]="true",["BrowserTls:Host"]=Host,["BrowserTls:Store"]="/var/lib/it-tls",["BrowserTls:Ingress"]=ingress,["BrowserTls:PublicPort"]=port }).Build();
        Assert.True(BrowserTlsSettings.Read(Values("kernel443","443"),"/app").KernelRedirect);
        Assert.False(BrowserTlsSettings.Read(Values("direct","5445"),"/app").KernelRedirect);
        foreach (var (ingress,port) in new[] { ("proxy","443"),("kernel443","5445"),("direct","443"),("kernel443","5443") })
            Assert.Throws<InvalidOperationException>(()=>BrowserTlsSettings.Read(Values(ingress,port),"/app"));
    }
}
