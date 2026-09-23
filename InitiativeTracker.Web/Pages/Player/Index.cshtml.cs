using InitiativeTracker.Web.Data;
using InitiativeTracker.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.Player;

[Authorize(Roles = "Player")]
public class IndexModel : PageModel
{
    private readonly PlayerRegistryService
        _playerRegistry;

    public IndexModel(
        PlayerRegistryService playerRegistry)
    {
        _playerRegistry =
            playerRegistry;
    }

    public PlayerConnection Player
    {
        get;
        private set;
    } = null!;

    public IActionResult OnGet()
    {
        var playerClaim =
            User.FindFirst(
                "player_id")?.Value;

        if (!Guid.TryParse(
                playerClaim,
                out var playerId))
        {
            return RedirectToPage(
                "/Join/Index");
        }

        var player =
            _playerRegistry.GetPlayer(
                playerId);

        if (player is null ||
            !player.IsActive)
        {
            return RedirectToPage(
                "/Join/Index");
        }

        Player =
            player;

        _playerRegistry.UpdateLastSeen(
            playerId);

        return Page();
    }
}