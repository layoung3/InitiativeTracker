using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InitiativeTracker.Web.Pages.Join;

[Authorize(Roles = "Player")]
public class SuccessModel : PageModel
{
    public void OnGet()
    {
    }
}