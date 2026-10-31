using InitiativeTracker.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace InitiativeTracker.Web.Services;

/// <summary>Bounded presentation configuration; never interprets HTML, CSS or paths.</summary>
public sealed class BrandingService(IConfiguration configuration, IDbContextFactory<InitiativeTrackerDbContext> factory)
{
    public string ApplicationName => NormalizeName(Settings()?.ApplicationName ?? configuration["Branding:ApplicationName"]);
    public bool ShowApplicationLogo => Settings()?.ShowApplicationLogo ?? configuration.GetValue("Branding:ShowApplicationLogo", true);
    public string LogoUrl => "/branding/logo";

    private PresentationSettings? Settings()
    {
        using var db = factory.CreateDbContext();
        return db.PresentationSettings.AsNoTracking().SingleOrDefault(x => x.Id == 1);
    }

    public void Save(string name, bool showLogo)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80 || name.Any(char.IsControl))
            throw new MediaValidationException("Use an application name of 1–80 characters, without control characters.");
        using var db = factory.CreateDbContext();
        var row = db.PresentationSettings.Find(1);
        if (row is null) { row = new PresentationSettings(); db.PresentationSettings.Add(row); }
        row.ApplicationName = name.Trim();
        row.ShowApplicationLogo = showLogo;
        db.SaveChanges();
    }

    public static string NormalizeName(string? value)
    {
        var name = value?.Trim();
        return string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl)
            ? "Initiative Tracker" : name;
    }
}
