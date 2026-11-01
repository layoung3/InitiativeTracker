using InitiativeTracker.Web.Infrastructure;
using System.Security.Claims;
using InitiativeTracker.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace InitiativeTracker.Web.Services;

public static class MediaPaths
{
    public const string DefaultPortrait = "/images/default-character.svg";
    public const string DefaultBanner = "/images/default-banner.svg";
    public static string CharacterPortrait(Guid? id) => id.HasValue ? Url("character", id.Value, "portrait") : DefaultPortrait;
    public static string CharacterBanner(Guid id) => Url("character", id, "banner");
    public static string ProfilePortrait(Guid id) => Url("profile", id, "portrait");
    public static string CampaignBanner => Url("application", Guid.Empty, "banner");
    public static string Url(string scope, Guid id, string kind) => $"/media/{scope}/{id:D}/{kind}";
    public static bool IsAllowed(string scope, string kind) => (scope, kind) is
        ("character", "portrait" or "banner") or ("profile", "portrait") or ("application", "logo" or "banner");
}

public sealed class MediaPolicy(IDbContextFactory<InitiativeTrackerDbContext> factory, EncounterStateService encounter,
    GameSessionService session, PlayerRegistryService players, DeviceRegistryService devices, IHttpContextAccessor contexts)
{
    // Null is an intentional uniform denial: unauthorized IDs disclose no existence.
    public MediaSubject? Subject(ClaimsPrincipal user, string scope, Guid id, bool write)
    {
        if (!N9PresentationAuthority.HasCurrent(contexts.HttpContext)) return null;
        if (user.Identity?.IsAuthenticated != true) return null;
        if (!Guid.TryParse(user.FindFirstValue("session_id"), out var sessionId) || sessionId != session.CurrentSession.SessionId)
            return null;
        var dm = user.IsInRole("DM");
        var player = user.IsInRole("Player") && Guid.TryParse(user.FindFirstValue("player_profile_id"), out _);
        Guid.TryParse(user.FindFirstValue("player_profile_id"), out var caller);
        if (user.IsInRole("Player") && (!player
            || !Guid.TryParse(user.FindFirstValue("player_id"), out var playerId)
            || !Guid.TryParse(user.FindFirstValue("player_device_id"), out var credentialId)
            || !players.IsActivePlayer(playerId, sessionId) || !players.IsActivePlayerCredential(credentialId, caller))) return null;
        if (user.IsInRole("Display") && (!Guid.TryParse(user.FindFirstValue("device_id"), out var deviceId)
            || !devices.IsTrustedDisplay(deviceId))) return null;
        using var db = factory.CreateDbContext();
        if (scope == "application" && id == Guid.Empty)
            return dm || (!write && (player || user.IsInRole("Display"))) ? new("Campaign and application", null) : null;
        if (scope == "profile")
        {
            if (!dm && (!player || caller != id)) return null;
            var profile = db.PlayerProfiles.AsNoTracking().SingleOrDefault(x => x.Id == id);
            return profile is null ? null : new(profile.Name, id);
        }
        if (scope != "character") return null;
        var character = db.PlayerCharacters.AsNoTracking().SingleOrDefault(x => x.Id == id && x.IsActive);
        if (character is null) return null;
        if (dm || (player && caller == character.PlayerProfileId)) return new(character.Name, character.PlayerProfileId);
        if (write || (!player && !user.IsInRole("Display"))) return null;
        // Reuse the exact public projection, including its existing assigned-player exception.
        return encounter.GetDisplayState().Combatants.Any(x => x.PortraitUrl == MediaPaths.CharacterPortrait(id))
            ? new(character.Name, character.PlayerProfileId) : null;
    }
}

public sealed record MediaSubject(string Name, Guid? OwnerProfileId);
