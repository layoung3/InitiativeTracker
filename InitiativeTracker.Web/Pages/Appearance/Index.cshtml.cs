using InitiativeTracker.Web.Hubs;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.RateLimiting;

namespace InitiativeTracker.Web.Pages.Appearance;

[Authorize(Roles = "DM,Player")]
[EnableRateLimiting("media-forms")]
[RequestSizeLimit(6 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 5 * 1024 * 1024 + 65536)]
public sealed class IndexModel(MediaPolicy policy, MediaStore store, BrandingService branding,
    IWebHostEnvironment environment, IHubContext<EncounterHub> hub, ILogger<IndexModel> logger) : PageModel
{
    public string Scope { get; private set; } = "";
    public Guid SubjectId { get; private set; }
    public string SubjectName { get; private set; } = "";
    public string[] Kinds => Scope == "application" ? ["logo", "banner"] : Scope == "profile" ? ["portrait"] : ["portrait", "banner"];
    public bool RequiresHttps => !Request.IsHttps && !environment.IsDevelopment();
    public bool HasPersistentStorage => store.AllowsPersistentWrites;
    [BindProperty] public IFormFile? Upload { get; set; }
    [BindProperty] public string? ApplicationName { get; set; }
    [BindProperty] public bool ShowApplicationLogo { get; set; }

    private bool Load(string scope, Guid id)
    {
        var subject = policy.Subject(User, scope, id, write: true);
        if (subject is null) return false;
        Scope = scope;
        SubjectId = id;
        SubjectName = subject.Name;
        if (Request.Query["handler"] != "Settings")
        {
            ApplicationName = branding.ApplicationName;
            ShowApplicationLogo = branding.ShowApplicationLogo;
        }
        return true;
    }

    public IActionResult OnGet(string scope, Guid subjectId)
    {
        if (!Load(scope, subjectId)) return NotFound();
        ApplicationName = branding.ApplicationName;
        ShowApplicationLogo = branding.ShowApplicationLogo;
        return Page();
    }

    public async Task<IActionResult> OnPostUploadAsync(string scope, Guid subjectId, string kind, CancellationToken cancellationToken)
    {
        if (RequiresHttps) return StatusCode(426);
        if (!Load(scope, subjectId) || !MediaPaths.IsAllowed(scope, kind)) return NotFound();
        try
        {
            if (Upload is null) throw new MediaValidationException("Choose a PNG or JPEG image.");
            await store.ReplaceAsync(scope, subjectId, kind, Upload,
                () => policy.Subject(User, scope, subjectId, write: true), cancellationToken);
            return await Changed(scope, subjectId);
        }
        catch (UnauthorizedAccessException) { return NotFound(); }
        catch (MediaValidationException exception) { return Failure(exception.Message, 400); }
        catch (MediaBusyException exception) { return Failure(exception.Message, 429); }
        catch (Exception exception) when (exception is IOException or Microsoft.Data.Sqlite.SqliteException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            logger.LogWarning(exception, "Image update could not be saved.");
            return Failure("Image could not be saved. Try again.", 503);
        }
    }

    public async Task<IActionResult> OnPostRemoveAsync(string scope, Guid subjectId, string kind, CancellationToken cancellationToken)
    {
        if (RequiresHttps) return StatusCode(426);
        if (!Load(scope, subjectId) || !MediaPaths.IsAllowed(scope, kind)) return NotFound();
        try
        {
            await store.RemoveAsync(scope, subjectId, kind, () => policy.Subject(User, scope, subjectId, write: true), cancellationToken);
            return await Changed(scope, subjectId);
        }
        catch (UnauthorizedAccessException) { return NotFound(); }
        catch (MediaBusyException exception) { return Failure(exception.Message, 429); }
        catch (MediaValidationException exception) { return Failure(exception.Message, 400); }
        catch (Exception exception) when (exception is IOException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            logger.LogWarning(exception, "Image removal could not be saved.");
            return Failure("Image could not be removed. Try again.",503);
        }
    }

    public async Task<IActionResult> OnPostSettingsAsync(string scope, Guid subjectId)
    {
        if (RequiresHttps) return StatusCode(426);
        if (!Load(scope, subjectId) || scope != "application") return NotFound();
        if (!HasPersistentStorage) return Failure("Configure persistent Tracker:DataDirectory outside the application directory before saving branding.",400);
        try { branding.Save(ApplicationName ?? "", ShowApplicationLogo); return await Changed(scope, subjectId); }
        catch (MediaValidationException exception) { return Failure(exception.Message, 400); }
    }

    private IActionResult Failure(string message, int status)
    {
        ModelState.AddModelError("", message);
        Response.StatusCode = status;
        return Page();
    }

    private async Task<IActionResult> Changed(string scope, Guid id)
    {
        // Do not broadcast private character/profile identifiers or original filenames.
        await hub.Clients.All.SendAsync("MediaUpdated");
        TempData["AppearanceNotice"] = "Appearance saved.";
        return RedirectToPage("/Appearance/Index", new { scope, subjectId = id });
    }
}
