using InitiativeTracker.Web.Services;
using InitiativeTracker.Web.Hubs;
using InitiativeTracker.Web.Data;
using Microsoft.EntityFrameworkCore;

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

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.MapHub<EncounterHub>("/hubs/encounter");

app.Run();
