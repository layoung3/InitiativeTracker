using System.Security.Cryptography.X509Certificates;

namespace InitiativeTracker.Core.Coordination;

/// <summary>Replaceable certificate source for browser provisioning; agents use separate physical pins.</summary>
public interface IProvisioningHttps
{
    bool Ready { get; }
    string? Host { get; }
    X509Certificate2? Certificate { get; }
}

public sealed class PublicCertificateHttps : IProvisioningHttps
{
    public string? Host { get; }
    public X509Certificate2? Certificate { get; }
    public bool Ready
    {
        get
        {
            if (Certificate is null || !Certificate.HasPrivateKey || Host is null || DateTime.UtcNow < Certificate.NotBefore ||
                DateTime.UtcNow >= Certificate.NotAfter || !Certificate.MatchesHostname(Host, allowWildcards: false, allowCommonName: false)) return false;
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.ApplicationPolicy.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1"));
            // No private trust roots, acceptance callbacks or certificate-warning bypass.
            // Offline operation uses OS public trust and certificate validity, without an online revocation dependency.
            return chain.Build(Certificate);
        }
    }
    public PublicCertificateHttps(string? host, string? certificatePath, string? password = null)
    {
        Host = host;
        if (!string.IsNullOrWhiteSpace(certificatePath)) Certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password);
    }
}
