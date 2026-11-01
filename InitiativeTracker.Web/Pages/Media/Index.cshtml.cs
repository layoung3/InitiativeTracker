using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.Media;

[Authorize(Roles = "DM,Player,Display")]
public sealed class IndexModel(MediaPolicy policy, MediaStore store, IWebHostEnvironment environment) : PageModel
{
    public IActionResult OnGet(string scope, Guid subjectId, string kind)
    {
        Response.Headers.CacheControl = "private, no-store, max-age=0";
        Response.Headers.XContentTypeOptions = "nosniff";
        if (!Request.IsHttps && !environment.IsDevelopment()) return StatusCode(426);
        if (!MediaPaths.IsAllowed(scope, kind) || policy.Subject(User, scope, subjectId, write: false) is null) return NotFound();
        var bytes = store.Read(scope, subjectId, kind);
        return bytes is not null ? File(bytes, "image/png")
            : Redirect(kind == "banner" ? MediaPaths.DefaultBanner : kind == "logo" ? "/branding/logo" : MediaPaths.DefaultPortrait);
    }
}
