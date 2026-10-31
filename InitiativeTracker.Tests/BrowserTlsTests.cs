using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using InitiativeTracker.Web.Infrastructure;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace InitiativeTracker.Tests;

public sealed class BrowserTlsTests
{
    private const string Host = "hub.owner-domain.org";

    [Theory]
    [InlineData(Host, true)]
    [InlineData("hub-2.sub.domain.org", true)]
    [InlineData("hub.local", false)]
    [InlineData("192.168.4.1", false)]
    [InlineData("hub.home.arpa", false)]
    [InlineData("hub.internal", false)]
    [InlineData("hub.domain.org:5445", false)]
    [InlineData("*.domain.org", false)]
    [InlineData("https://hub.domain.org", false)]
    [InlineData("hub.domain.org.", false)]
    [InlineData("HUB.domain.org", false)]
    [InlineData("a..domain.org", false)]
    [InlineData("-hub.domain.org", false)]
    [InlineData("hub.域名.org", false)]
    public void HostnameValidation(string host, bool valid) => Assert.Equal(valid, BrowserTlsSettings.ValidHostname(host));

    [Fact]
    public void PublicTrustRejectsFixtureRootAndAcceptsOnlyExplicitUnitTestRoot()
    {
        using var fixture = new Certificates();
        using var leaf = fixture.Leaf(Host);
        Assert.False(BrowserCertificateStore.Validate(leaf, [], Host));
        Assert.True(BrowserCertificateStore.Validate(leaf, [], Host, fixture.Root));
        Assert.False(BrowserCertificateStore.Validate(leaf, [], "other.domain.org", fixture.Root));
    }

    [Fact]
    public void ExpiredFutureWildcardCnFallbackAndClientOnlyCertificatesFail()
    {
        using var fixture = new Certificates();
        using var expired = fixture.Leaf(Host, start: -10, end: -5);
        using var future = fixture.Leaf(Host, start: 1, end: 2);
        using var wildcard = fixture.Leaf("*.owner-domain.org");
        using var noSan = fixture.Leaf(Host, noSan: true);
        using var clientOnly = fixture.Leaf(Host, clientOnly: true);
        foreach (var leaf in new[] { expired, future, wildcard, noSan, clientOnly })
            Assert.False(BrowserCertificateStore.Validate(leaf, [], Host, fixture.Root));
    }

    [Fact]
    public void ClockAndExpirationAreCheckedAtTimeOfUse()
    {
        using var fixture = new Certificates();
        using var leaf = fixture.Leaf(Host, start: -1, end: 1);
        Assert.True(BrowserCertificateStore.Validate(leaf, [], Host, fixture.Root));
        Assert.False(BrowserCertificateStore.Validate(leaf, [], Host, fixture.Root, DateTime.UtcNow.AddDays(2)));
        Assert.False(BrowserCertificateStore.Validate(leaf, [], Host, fixture.Root, DateTime.UtcNow.AddDays(-2)));
    }

    [Fact]
    public void PublicKeyOnlyCannotBeServed()
    {
        using var fixture = new Certificates();
        using var leaf = fixture.Leaf(Host);
        using var noKey = X509CertificateLoader.LoadCertificate(leaf.Export(X509ContentType.Cert));
        Assert.False(BrowserCertificateStore.Validate(noKey, [], Host, fixture.Root));
    }

    [Theory]
    [InlineData("/media", "GET", "", true)]
    [InlineData("/media/123", "GET", "", true)]
    [InlineData("/appearance", "POST", "Upload", true)]
    [InlineData("/Appearance", "POST", "Settings", true)]
    [InlineData("/appearance", "GET", "", false)]
    [InlineData("/dm/network", "POST", "pRoViSiOn", true)]
    [InlineData("/dm/network", "POST", "RotateHotspotPassword", true)]
    [InlineData("/dm/network", "POST", "UnlockNetwork", true)]
    [InlineData("/dm/network", "POST", "Scan", false)]
    [InlineData("/player/character", "POST", "UploadPortrait", true)]
    [InlineData("/player/character", "POST", "Roll", false)]
    [InlineData("/display/pair", "GET", "", false)]
    public void FutureIntegrationRoutesFailClosedWithoutAffectingGameplay(string path, string method, string handler, bool sensitive)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Request.QueryString = new QueryString("?handler=" + handler);
        Assert.Equal(sensitive, BrowserTls.Sensitive(context.Request));
    }

    [Fact]
    public void UnsafeConfigurationCannotEnableTls()
    {
        IConfiguration Values(params (string Key, string Value)[] extra) => new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["BrowserTls:Enabled"] = "true", ["BrowserTls:Host"] = Host,
                ["BrowserTls:Store"] = "/var/lib/it-browser-tls" }).AddInMemoryCollection(extra.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value))).Build();
        Assert.Throws<InvalidOperationException>(() => BrowserTlsSettings.Read(Values(("BrowserTls:Port", "5443")), "/app"));
        Assert.Throws<InvalidOperationException>(() => BrowserTlsSettings.Read(Values(("Tracker:UseHttpsRedirection", "true")), "/app"));
        Assert.Throws<InvalidOperationException>(() => BrowserTlsSettings.Read(Values(("BrowserTls:Store", "/app/private")), "/app"));
    }

    [Fact]
    public void RuntimeCertificatePathsRejectBroadPermissionsAndLinks()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "https-paths-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var bundle = Path.Combine(root, "bundle.pfx");
        try
        {
            File.WriteAllText(bundle, "fixture");
            File.SetUnixFileMode(bundle, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            BrowserCertificateStore.CheckPath(bundle, false);
            File.SetUnixFileMode(bundle, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
            Assert.Throws<InvalidOperationException>(() => BrowserCertificateStore.CheckPath(bundle, false));
            File.SetUnixFileMode(bundle, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var link = Path.Combine(root, "active.json");
            File.CreateSymbolicLink(link, bundle);
            Assert.Throws<InvalidOperationException>(() => BrowserCertificateStore.CheckPath(link, false));
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupWrite);
            Assert.Throws<InvalidOperationException>(() => BrowserCertificateStore.CheckPath(bundle, false));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task HttpAndSpoofedForwardedHeadersCannotUnlockSensitiveOperations()
    {
        var directory = Path.Combine(Path.GetTempPath(), "https-web-" + Guid.NewGuid());
        try
        {
            using var app = new WebApplicationFactory<EncounterStateService>().WithWebHostBuilder(builder => builder
                .UseEnvironment("Production").UseSetting("Tracker:DataDirectory", Path.Combine(directory, "data"))
                .UseSetting("BrowserTls:Enabled", "true").UseSetting("BrowserTls:Host", Host)
                .UseSetting("BrowserTls:Store", Path.Combine(directory, "certificates")));
            using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
            client.DefaultRequestHeaders.Add("X-Forwarded-Host", Host + ":5445");
            foreach (var path in new[] { "/appearance?handler=Upload", "/appearance?handler=Settings", "/dm/network?handler=provision" })
            {
                var response = await client.PostAsync(path, new StringContent("fixture-sensitive-body"));
                Assert.Equal((HttpStatusCode)426, response.StatusCode);
                Assert.Null(response.Headers.Location);
                Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            }
            Assert.Equal((HttpStatusCode)426, (await client.GetAsync("/media?id=fixture")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/system/health")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/display/pair")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/dm/login")).StatusCode);
            client.DefaultRequestHeaders.Host = Host + ":5000";
            Assert.Equal((HttpStatusCode)426, (await client.GetAsync("/dm/login")).StatusCode);
            Assert.Equal((HttpStatusCode)426, (await client.PostAsync("/dm/login", new StringContent("fixture-pin"))).StatusCode);
            client.DefaultRequestHeaders.Host = null;
            var status = await client.GetStringAsync("/system/https-status");
            Assert.Contains("\"ready\":false", status);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class Certificates : IDisposable
    {
        private readonly RSA rootKey = RSA.Create(2048);
        public X509Certificate2 Root { get; }
        public Certificates()
        {
            var request = new CertificateRequest("CN=Disposable Test Root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
            Root = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-365), DateTimeOffset.UtcNow.AddDays(365));
        }
        public X509Certificate2 Leaf(string host, int start = -1, int end = 2, bool noSan = false, bool clientOnly = false)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=" + host, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (!noSan)
            {
                var san = new SubjectAlternativeNameBuilder(); san.AddDnsName(host);
                request.CertificateExtensions.Add(san.Build());
            }
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(clientOnly ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1") }, true));
            using var leaf = request.Create(Root, DateTimeOffset.UtcNow.AddDays(start), DateTimeOffset.UtcNow.AddDays(end), RandomNumberGenerator.GetBytes(16));
            return leaf.CopyWithPrivateKey(key);
        }
        public void Dispose() { Root.Dispose(); rootKey.Dispose(); }
    }
}
