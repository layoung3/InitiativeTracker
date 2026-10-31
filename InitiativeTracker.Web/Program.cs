using InitiativeTracker.Web.Services;
using InitiativeTracker.Web.Infrastructure;
using InitiativeTracker.Web.Hubs;
using InitiativeTracker.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
BrowserTls.Configure(builder);

// Add services to the container.
builder.Services.AddRazorPages();

builder.Services.AddSignalR();

var configuredDataDirectory =
    builder.Configuration[
        "Tracker:DataDirectory"];

var dataDirectory =
    string.IsNullOrWhiteSpace(
        configuredDataDirectory)
        ? Path.Combine(
            builder.Environment.ContentRootPath,
            "App_Data")
        : Path.IsPathRooted(
            configuredDataDirectory)
            ? configuredDataDirectory
            : Path.GetFullPath(
                Path.Combine(
                    builder.Environment.ContentRootPath,
                    configuredDataDirectory));

Directory.CreateDirectory(
    dataDirectory);

var dataProtectionDirectory =
    Path.Combine(
        dataDirectory,
        "DataProtection-Keys");

Directory.CreateDirectory(
    dataProtectionDirectory);

builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(
        new DirectoryInfo(
            dataProtectionDirectory))
    .SetApplicationName(
        "InitiativeTracker");

var databasePath = Path.Combine(
    dataDirectory,
    "initiative-tracker.db");

builder.Services.AddDbContextFactory<InitiativeTrackerDbContext>(
    options =>
        options.UseSqlite(
            $"Data Source={databasePath}"));

builder.Services.AddSingleton<EncounterStateService>();
builder.Services.AddSingleton<GameSessionService>();
builder.Services.AddSingleton<DeviceRegistryService>();
builder.Services.AddSingleton<PlayerRegistryService>();
builder.Services.AddSingleton<HubIdentityService>();
builder.Services.AddSingleton<TrackerUrlService>();
builder.Services.AddSingleton<NetworkStatusService>();
builder.Services.AddSingleton<NetworkCommandBridge>();
builder.Services.AddSingleton<CharacterService>();
builder.Services.AddSingleton<AppVersionService>();

builder.Services.AddSingleton(
    serviceProvider =>
        new SystemHealthService(
            serviceProvider
                .GetRequiredService<
                    IDbContextFactory<
                        InitiativeTrackerDbContext>>(),

            serviceProvider
                .GetRequiredService<
                    AppVersionService>(),

            serviceProvider
                .GetRequiredService<
                    HubIdentityService>(),

            dataDirectory));

builder.Services
    .AddAuthentication(
        CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath =
            "/dm/login";

        options.AccessDeniedPath =
            "/access-denied";

        options.Cookie.Name =
            "InitiativeTracker.Auth";

        options.Cookie.HttpOnly =
            true;

        options.Cookie.SameSite =
            SameSiteMode.Lax;

        options.Cookie.SecurePolicy =
            CookieSecurePolicy.SameAsRequest;

        options.ExpireTimeSpan =
            TimeSpan.FromHours(12);

        options.SlidingExpiration =
            true;

        options.Events.OnRedirectToLogin =
            context =>
            {
                var path =
                    context.Request.Path;

                if (path.StartsWithSegments("/display"))
                {
                    context.Response.Redirect(
                        "/display/pair");
                }
                else if (path.StartsWithSegments("/dm"))
                {
                    context.Response.Redirect(
                        "/dm/login");
                }
                else
                {
                    context.Response.Redirect(
                        "/join");
                }

                return Task.CompletedTask;
            };

        options.Events.OnRedirectToAccessDenied =
            context =>
            {
                context.Response.Redirect(
                    "/access-denied");

                return Task.CompletedTask;
            };

        options.Events.OnValidatePrincipal =
            async context =>
            {
                var principal =
                    context.Principal;

                if (principal is null)
                {
                    return;
                }

                var gameSession =
                    context.HttpContext
                        .RequestServices
                        .GetRequiredService<
                            GameSessionService>();

                var sessionClaim =
                    principal.FindFirst(
                        "session_id")?.Value;

                if (!Guid.TryParse(
                        sessionClaim,
                        out var sessionId) ||
                    sessionId !=
                        gameSession.CurrentSession.SessionId)
                {
                    context.RejectPrincipal();

                    await context.HttpContext
                        .SignOutAsync();

                    return;
                }


                if (principal.IsInRole("Player"))
                {
                    var playerClaim =
                        principal.FindFirst(
                            "player_id")?.Value;

                    var profileClaim =
                        principal.FindFirst(
                            "player_profile_id")?.Value;

                    var credentialClaim =
                        principal.FindFirst(
                            "player_device_id")?.Value;

                    if (!Guid.TryParse(
                            playerClaim,
                            out var playerId) ||
                        !Guid.TryParse(
                            profileClaim,
                            out var playerProfileId) ||
                        !Guid.TryParse(
                            credentialClaim,
                            out var credentialId))
                    {
                        context.RejectPrincipal();

                        await context.HttpContext
                            .SignOutAsync();

                        return;
                    }

                    var players =
                        context.HttpContext
                            .RequestServices
                            .GetRequiredService<
                                PlayerRegistryService>();

                    if (!players.IsActivePlayer(
                            playerId,
                            sessionId) ||
                        !players.IsActivePlayerCredential(
                            credentialId,
                            playerProfileId))
                    {
                        context.RejectPrincipal();

                        await context.HttpContext
                            .SignOutAsync();

                        return;
                    }

                    players.UpdateLastSeen(
                        playerId);

                    return;
                }


                if (principal.IsInRole("Display"))
                {
                    var deviceClaim =
                        principal.FindFirst(
                            "device_id")?.Value;

                    if (!Guid.TryParse(
                            deviceClaim,
                            out var deviceId))
                    {
                        context.RejectPrincipal();

                        await context.HttpContext
                            .SignOutAsync();

                        return;
                    }

                    var registry =
                        context.HttpContext
                            .RequestServices
                            .GetRequiredService<
                                DeviceRegistryService>();

                    if (!registry.IsTrustedDisplay(
                            deviceId))
                    {
                        context.RejectPrincipal();

                        await context.HttpContext
                            .SignOutAsync();
                    }

                    return;
                }

                /*
                 * DM cookies only need the session validation above.
                 */
            };
    });

SecureAccess.Configure(builder, dataDirectory);
builder.Services.AddAuthorization();

var app = builder.Build();

using (var db =
       app.Services
           .GetRequiredService<
               IDbContextFactory<InitiativeTrackerDbContext>>()
           .CreateDbContext())
{
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    // HSTS is deliberately deferred during the dual-origin HTTPS migration.
    if (!app.Services.GetRequiredService<BrowserTlsSettings>().Enabled) app.UseHsts();
}

if (builder.Configuration
    .GetValue<bool>(
        "Tracker:UseHttpsRedirection"))
{
    app.UseHttpsRedirection();
}

app.Use(BrowserTls.Guard);

app.UseRouting();

app.UseAuthentication();
app.Use(SecureAccess.Guard);
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapHub<EncounterHub>("/hubs/encounter");


app.MapGet(
    "/system/hub-info",
    (
        HubIdentityService hubIdentity,
        IConfiguration configuration) =>
    {
        var port =
            configuration
                .GetValue<int?>(
                    "Tracker:LanPort") ??
            5000;

        return Results.Json(
            new
            {
                protocolVersion =
                    1,

                hubId =
                    hubIdentity.HubId,

                hostName =
                    hubIdentity.HostName,

                mdnsHostName =
                    hubIdentity.MdnsHostName,

                port,

                displayPairPath =
                    "/display/pair",

                playerJoinPath =
                    "/join"
            });
    })
    .AllowAnonymous();


app.MapGet(
    "/system/health",
    async (
        SystemHealthService healthService,
        CancellationToken cancellationToken) =>
    {
        var health =
            await healthService
                .CheckAsync(
                    cancellationToken);

        var response =
            new
            {
                status =
                    health.Healthy
                        ? "healthy"
                        : "unhealthy",

                checkedAtUtc =
                    health.CheckedAtUtc,

                product =
                    health.Product,

                version =
                    health.Version,

                hubId =
                    health.HubId,

                hubProtocolVersion =
                    health.HubProtocolVersion,

                database =
                    new
                    {
                        connected =
                            health.DatabaseConnected,

                        migrationsCurrent =
                            health.PendingMigrations.Count ==
                            0
                    },

                storage =
                    new
                    {
                        ready =
                            health.DataDirectoryReady
                    }
            };

        return health.Healthy
            ? Results.Ok(
                response)
            : Results.Json(
                response,
                statusCode:
                    StatusCodes
                        .Status503ServiceUnavailable);
    })
    .AllowAnonymous();


app.MapGet(
    "/system/status",
    async (
        SystemHealthService healthService,
        CancellationToken cancellationToken) =>
    {
        var health =
            await healthService
                .CheckAsync(
                    cancellationToken);

        return Results.Ok(
            new
            {
                product =
                    health.Product,

                version =
                    health.Version,

                informationalVersion =
                    health.InformationalVersion,

                assemblyVersion =
                    health.AssemblyVersion,

                status =
                    health.Healthy
                        ? "healthy"
                        : "unhealthy",

                checkedAtUtc =
                    health.CheckedAtUtc,

                startedAtUtc =
                    health.StartedAtUtc,

                uptimeSeconds =
                    Math.Max(
                        0,
                        (long)health
                            .Uptime
                            .TotalSeconds),

                hub =
                    new
                    {
                        id =
                            health.HubId,

                        hostName =
                            health.HubHostName,

                        protocolVersion =
                            health.HubProtocolVersion
                    },

                updater =
                    new
                    {
                        releaseManifestFormatVersion =
                            health
                                .ReleaseManifestFormatVersion
                    },

                database =
                    new
                    {
                        connected =
                            health.DatabaseConnected,

                        latestAppliedMigration =
                            health.LatestAppliedMigration,

                        pendingMigrationCount =
                            health.PendingMigrations.Count,

                        pendingMigrations =
                            health.PendingMigrations,

                        error =
                            health.DatabaseError
                    },

                storage =
                    new
                    {
                        ready =
                            health.DataDirectoryReady,

                        availableBytes =
                            health.AvailableStorageBytes
                    },

                runtime =
                    new
                    {
                        framework =
                            health.Runtime,

                        architecture =
                            health.Architecture,

                        operatingSystem =
                            health.OperatingSystem
                    }
            });
    })
    .AllowAnonymous();


app.MapPost(
        "/player/presence",
        (
            HttpContext context,
            PlayerRegistryService players) =>
        {
            var playerClaim =
                context.User
                    .FindFirst(
                        "player_id")?
                    .Value;

            if (!Guid.TryParse(
                    playerClaim,
                    out var playerId))
            {
                return Results.Unauthorized();
            }

            players.UpdateLastSeen(
                playerId);

            return Results.NoContent();
        })
    .RequireAuthorization(
        policy =>
            policy.RequireRole(
                "Player"));

app.MapGet("/system/https-status", (HttpContext context, BrowserCertificateStore certificates) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Json(new { ready = certificates.TrustedRequest(context), generation = certificates.Generation, expiresAtUtc = certificates.ExpiresAtUtc });
}).AllowAnonymous();

app.Run();
