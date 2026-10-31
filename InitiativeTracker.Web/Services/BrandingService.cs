namespace InitiativeTracker.Web.Services;

/// <summary>Bounded presentation configuration; never interprets HTML, CSS or paths.</summary>
public sealed class BrandingService(IConfiguration configuration, IWebHostEnvironment environment)
{
    public string ApplicationName { get; } = NormalizeName(configuration["Branding:ApplicationName"]);
    public bool ShowApplicationLogo { get; } = configuration.GetValue("Branding:ShowApplicationLogo", true);
    public bool ColophonAvailable => File.Exists(Path.Combine(environment.WebRootPath, "images", "colophon.png"));
    public string? LogoUrl => ColophonAvailable ? "/images/colophon.png" : null;

    public static string NormalizeName(string? value)
    {
        var name = value?.Trim();
        return string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl)
            ? "Initiative Tracker" : name;
    }
}
