using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace InitiativeTracker.Web.Infrastructure;

public sealed record BrowserTlsSettings(bool Enabled, string Host, int Port, string Store)
{
    public static bool ValidHostname(string? host) => host is { Length: <= 253 } && host == host.ToLowerInvariant() &&
        host.Split('.') is { Length: >= 3 } labels && labels.All(label => Regex.IsMatch(label, @"^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")) &&
        !labels[^1].All(char.IsDigit) && !new[] { "local", "localhost", "test", "invalid", "example", "internal", "lan", "home", "arpa", "onion" }.Contains(labels[^1]);

    public static BrowserTlsSettings Read(IConfiguration configuration, string contentRoot)
    {
        var enabled = configuration.GetValue<bool>("BrowserTls:Enabled");
        var host = configuration["BrowserTls:Host"] ?? "";
        var port = configuration.GetValue<int?>("BrowserTls:Port") ?? 5445;
        var store = configuration["BrowserTls:Store"] ?? "";
        if (enabled && (!OperatingSystem.IsLinux() || !ValidHostname(host) || port != 5445 ||
            !Path.IsPathFullyQualified(store) || Path.GetFullPath(store).StartsWith(Path.GetFullPath(contentRoot) + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.GetFullPath(store) == Path.GetFullPath(contentRoot) || store == "/" || configuration.GetValue<bool>("Tracker:UseHttpsRedirection")))
            throw new InvalidOperationException("Browser TLS requires Linux, a public FQDN, port 5445, a separate persistent store and redirection disabled.");
        return new(enabled, host, port, store);
    }
}

/// <summary>Public browser trust only. Never used as an agent enrollment pin or DNS credential store.</summary>
public sealed class BrowserCertificateStore(BrowserTlsSettings settings, ILogger<BrowserCertificateStore> logger) : IDisposable
{
    private readonly object gate = new();
    private readonly List<CertificateGeneration> generations = [];
    private CertificateGeneration? current;
    public DateTime? ExpiresAtUtc { get { lock (gate) return current?.Certificate.NotAfter.ToUniversalTime(); } }
    public string? Generation { get { lock (gate) return current?.Id; } }
    public bool Ready { get { lock (gate) return current is not null && Validate(current.Certificate, current.Chain, settings.Host); } }
    public BrowserTlsSettings Settings => settings;

    public static bool Validate(X509Certificate2 certificate, X509Certificate2Collection intermediates, string host,
        X509Certificate2? testRoot = null, DateTime? testTime = null)
    {
        if (!BrowserTlsSettings.ValidHostname(host) || !certificate.HasPrivateKey ||
            !certificate.MatchesHostname(host, allowWildcards: false, allowCommonName: false)) return false;
        var san = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().SingleOrDefault();
        if (san is null || !san.EnumerateDnsNames().SequenceEqual([host]) || san.EnumerateIPAddresses().Any()) return false;
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.ExtraStore.AddRange(intermediates);
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        if (testTime.HasValue) chain.ChainPolicy.VerificationTime = testTime.Value;
        // Unit-test injection only. Production loading has no custom-root configuration.
        if (testRoot is not null)
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(testRoot);
        }
        return chain.Build(certificate);
    }

    public void Reload()
    {
        if (!settings.Enabled) return;
        lock (gate)
        {
            try
            {
                CheckPath(settings.Store, true);
                var manifest = Path.Combine(settings.Store, "active.json");
                CheckPath(manifest, false);
                if (new FileInfo(manifest).Length > 256) throw new InvalidOperationException();
                using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                var id = document.RootElement.GetProperty("generation").GetString() ?? "";
                if (!Regex.IsMatch(id, "^[a-f0-9]{32}$")) throw new InvalidOperationException();
                if (current?.Id == id) return;
                // Retain certificate handles for in-flight TLS handshakes until shutdown. Bound memory.
                var cached = generations.FirstOrDefault(x => x.Id == id);
                if (cached is not null && Validate(cached.Certificate, cached.Chain, settings.Host)) { current = cached; return; }
                if (generations.Count >= 16) throw new InvalidOperationException();
                var folder = Path.Combine(settings.Store, id);
                CheckPath(folder, true);
                var bundle = Path.Combine(folder, "bundle.pfx");
                CheckPath(bundle, false);
                if (new FileInfo(bundle).Length > 262144) throw new InvalidOperationException();
                var collection = X509CertificateLoader.LoadPkcs12CollectionFromFile(bundle, "", X509KeyStorageFlags.EphemeralKeySet);
                var certificate = collection.Cast<X509Certificate2>().SingleOrDefault(c => c.HasPrivateKey);
                var chain = new X509Certificate2Collection(collection.Cast<X509Certificate2>().Where(c => !ReferenceEquals(c, certificate)).ToArray());
                if (certificate is null || !Validate(certificate, chain, settings.Host))
                {
                    foreach (var item in collection) item.Dispose();
                    throw new InvalidOperationException();
                }
                var next = new CertificateGeneration(id, certificate, chain, SslStreamCertificateContext.Create(certificate, chain, offline: true));
                generations.Add(next);
                current = next;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException or JsonException or KeyNotFoundException)
            {
                // Do not log exception text, paths, keys, provider output or certificate contents.
                logger.LogWarning("Browser certificate reload rejected; retaining any valid previous generation.");
            }
        }
    }

    public SslServerAuthenticationOptions Handshake(string? serverName)
    {
        lock (gate)
        {
            if (!string.Equals(serverName, settings.Host, StringComparison.OrdinalIgnoreCase) || current is null || !Validate(current.Certificate, current.Chain, settings.Host))
                throw new AuthenticationException("Trusted browser HTTPS is unavailable.");
            return new SslServerAuthenticationOptions
            {
                ServerCertificateContext = current.Context,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                ApplicationProtocols = [SslApplicationProtocol.Http2, SslApplicationProtocol.Http11],
                ClientCertificateRequired = false,
                AllowRenegotiation = false
            };
        }
    }

    public bool TrustedRequest(HttpContext context) => settings.Enabled && Ready && context.Request.IsHttps &&
        context.Features.Get<ITlsHandshakeFeature>() is not null && context.Connection.LocalPort == settings.Port &&
        (context.Request.Host.Port ?? 443) == settings.Port && string.Equals(context.Request.Host.Host, settings.Host, StringComparison.OrdinalIgnoreCase);

    public static void CheckPath(string path, bool directory)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException("Absolute path required.");
        FileSystemInfo item = directory ? new DirectoryInfo(path) : new FileInfo(path);
        for (FileSystemInfo? parent = item; parent is not null; parent = parent is DirectoryInfo d ? d.Parent : ((FileInfo)parent).Directory)
        {
            if (!parent.Exists || parent.LinkTarget is not null) throw new InvalidOperationException("Unsafe certificate path.");
            if (OperatingSystem.IsLinux())
            {
                var mode = File.GetUnixFileMode(parent.FullName);
                var writable = mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite);
                if (writable != 0 && !(parent is DirectoryInfo && mode.HasFlag(UnixFileMode.StickyBit))) throw new InvalidOperationException("Writable certificate path.");
            }
        }
        if (OperatingSystem.IsLinux())
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.OtherRead | UnixFileMode.OtherExecute | UnixFileMode.OtherWrite | UnixFileMode.GroupWrite)) != 0)
                throw new InvalidOperationException("Certificate permissions are too broad.");
        }
    }

    public void Dispose() { foreach (var item in generations) { item.Certificate.Dispose(); foreach (var certificate in item.Chain) certificate.Dispose(); } }
    private sealed record CertificateGeneration(string Id, X509Certificate2 Certificate, X509Certificate2Collection Chain, SslStreamCertificateContext Context);
}

public sealed class BrowserCertificateReloader(BrowserCertificateStore store) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        do { store.Reload(); } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public static class BrowserTls
{
    public static void Configure(WebApplicationBuilder builder)
    {
        var settings = BrowserTlsSettings.Read(builder.Configuration, builder.Environment.ContentRootPath);
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton<BrowserCertificateStore>();
        if (!settings.Enabled) return;
        builder.Services.AddHostedService<BrowserCertificateReloader>();
        builder.WebHost.ConfigureKestrel((context, options) =>
        {
            // Preserve configured HTTP endpoints; keep the normal fallback when no endpoints exist.
            if (!context.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
                options.ListenAnyIP(context.Configuration.GetValue<int?>("Tracker:LanPort") ?? 5000);
            options.ListenAnyIP(settings.Port, listen => listen.UseHttps(new TlsHandshakeCallbackOptions
            {
                OnConnection = connection => new ValueTask<SslServerAuthenticationOptions>(
                    options.ApplicationServices.GetRequiredService<BrowserCertificateStore>().Handshake(connection.ClientHelloInfo.ServerName))
            }));
        });
    }

    public static bool Sensitive(HttpRequest request)
    {
        var path = request.Path;
        if (path.StartsWithSegments("/media")) return true;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method)) return false;
        if (path.StartsWithSegments("/appearance")) return true;
        var handler = request.Query["handler"].ToString().ToUpperInvariant();
        return (path.StartsWithSegments("/player/character") || path.StartsWithSegments("/player/editcharacter")) &&
            handler is "UPLOADPORTRAIT" or "REMOVEPORTRAIT" or "UPLOADBANNER" or "REMOVEBANNER" ||
            path.StartsWithSegments("/dm/network") && handler is "PROVISION" or "VALIDATE" or "ROTATEHOTSPOTPASSWORD" or "UNLOCKNETWORK";
    }

    public static async Task Guard(HttpContext context, RequestDelegate next)
    {
        var store = context.RequestServices.GetRequiredService<BrowserCertificateStore>();
        if (store.Settings.Enabled && context.Connection.LocalPort == store.Settings.Port && !store.TrustedRequest(context))
        { context.Response.StatusCode = 421; context.Response.Headers.CacheControl = "no-store"; return; }
        if (store.Settings.Enabled && Sensitive(context.Request) && !store.TrustedRequest(context))
        { context.Response.StatusCode = 426; context.Response.Headers.CacheControl = "no-store"; return; }
        await next(context);
    }
}
