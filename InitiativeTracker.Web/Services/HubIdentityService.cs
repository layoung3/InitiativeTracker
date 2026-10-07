using InitiativeTracker.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace InitiativeTracker.Web.Services;

public sealed class HubIdentityService
{
    private const int SettingsRowId =
        1;

    private readonly object _lock =
        new();

    private readonly IDbContextFactory<InitiativeTrackerDbContext>
        _dbContextFactory;

    private readonly IConfiguration
        _configuration;

    public Guid HubId { get; private set; }

    public string HostName { get; private set; } =
        string.Empty;

    public string MdnsHostName =>
        $"{HostName}.local";


    public HubIdentityService(
        IDbContextFactory<InitiativeTrackerDbContext> dbContextFactory,
        IConfiguration configuration)
    {
        _dbContextFactory =
            dbContextFactory;

        _configuration =
            configuration;

        LoadOrCreateIdentity();
    }


    private void LoadOrCreateIdentity()
    {
        lock (_lock)
        {
            using var db =
                _dbContextFactory
                    .CreateDbContext();

            var settings =
                db.ApplicationSettings
                    .SingleOrDefault(
                        x =>
                            x.Id ==
                            SettingsRowId);

            if (settings is null)
            {
                settings =
                    new ApplicationSettingsRecord
                    {
                        Id =
                            SettingsRowId,

                        DisplayTheme =
                            "dark"
                    };

                db.ApplicationSettings.Add(
                    settings);
            }


            var changed =
                false;

            if (settings.HubId ==
                Guid.Empty)
            {
                settings.HubId =
                    Guid.NewGuid();

                changed =
                    true;
            }


            if (string.IsNullOrWhiteSpace(
                    settings.HubHostName))
            {
                settings.HubHostName =
                    GenerateHostName(
                        settings.HubId);

                changed =
                    true;
            }


            if (changed)
            {
                settings.UpdatedAtUtc =
                    DateTime.UtcNow;

                db.SaveChanges();
            }


            HubId =
                settings.HubId;

            HostName =
                settings.HubHostName!;
        }
    }


    private string GenerateHostName(
        Guid hubId)
    {
        var prefix =
            _configuration[
                "Tracker:HubHostPrefix"];

        if (string.IsNullOrWhiteSpace(
                prefix))
        {
            prefix =
                "initiative";
        }


        var normalizedPrefix =
            NormalizeHostPart(
                prefix);

        if (string.IsNullOrWhiteSpace(
                normalizedPrefix))
        {
            normalizedPrefix =
                "initiative";
        }


        var suffix =
            hubId
                .ToString("N")
                [..8]
                .ToLowerInvariant();


        return
            $"{normalizedPrefix}-{suffix}";
    }


    private static string NormalizeHostPart(
        string value)
    {
        var normalized =
            new string(
                value
                    .Trim()
                    .ToLowerInvariant()
                    .Select(
                        character =>
                            char.IsAsciiLetterOrDigit(
                                character)
                                ? character
                                : '-')
                    .ToArray());

        while (normalized.Contains(
                   "--",
                   StringComparison.Ordinal))
        {
            normalized =
                normalized.Replace(
                    "--",
                    "-",
                    StringComparison.Ordinal);
        }

        return normalized.Trim('-');
    }
}
