using InitiativeTracker.Web.Services;
using InitiativeTracker.Web.Hubs;
using InitiativeTracker.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

builder.Services.AddSignalR();

var dataDirectory = Path.Combine(
    builder.Environment.ContentRootPath,
    "App_Data");

Directory.CreateDirectory(dataDirectory);

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
builder.Services.AddSingleton<TrackerUrlService>();

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

                if (!principal.IsInRole("Display"))
                {
                    return;
                }

                if (principal.IsInRole("Player"))
                {
                    var playerClaim =
                        principal.FindFirst(
                            "player_id")?.Value;

                    if (!Guid.TryParse(
                            playerClaim,
                            out var playerId))
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
                            sessionId))
                    {
                        context.RejectPrincipal();

                        await context.HttpContext
                            .SignOutAsync();

                        return;
                    }
                }

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
            };
    });

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
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapHub<EncounterHub>("/hubs/encounter");

app.Run();
