using Microsoft.Extensions.Hosting;

namespace InitiativeTracker.Web.Infrastructure;

/// <summary>N9 consumes shared authority; it never creates or promotes secure grants.</summary>
public static class N9PresentationAuthority
{
    public static bool HasCurrent(HttpContext? context)
    {
        if (context is null) return false;
        var origin = context.RequestServices.GetService<ITrustedBrowserOrigin>();
        if (origin is null) return false;
        // Explicit disposable fixture compatibility, never a production transport fallback.
        if (!origin.Enabled) return context.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();
        if (!origin.TrustedRequest(context) || context.Items[typeof(SecureGrant)] is not SecureGrant entry ||
            SecureAccess.Browser(context) is not { } browser) return false;
        var service = context.RequestServices.GetRequiredService<SecureAccessService>();
        var current = service.Validate(entry.Id, entry.Generation, entry.Epoch, browser, origin.Origin);
        return current is not null && service.MatchesLegacy(current, context.User) && service.MediaOwnershipUnchanged(context);
    }
    public static int Commit(HttpContext context, Func<int> commit)
    {
        var origin = context.RequestServices.GetService<ITrustedBrowserOrigin>();
        if (origin is null) throw new SecureAccessDenied();
        if (!origin.Enabled && context.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
            return commit();
        if (!origin.Enabled || !origin.TrustedRequest(context) || context.Items[typeof(SecureGrant)] is not SecureGrant entry ||
            SecureAccess.Browser(context) is not { } browser) throw new SecureAccessDenied();
        return context.RequestServices.GetRequiredService<SecureAccessService>()
            .CommitAuthorized(entry, browser, origin.Origin, context.User, commit);
    }

}

/// <summary>Thin injectable adapter; all authority stays in SecureAccessService.</summary>
public class N9PresentationCommitter(IHttpContextAccessor contexts)
{
    public virtual int Commit(Func<int> commit) => N9PresentationAuthority.Commit(
        contexts.HttpContext ?? throw new SecureAccessDenied(), commit);
}
