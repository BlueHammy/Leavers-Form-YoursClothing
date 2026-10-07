using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LeaversForm.Web.Pages.Account;

[AllowAnonymous]
public class SignedOutModel : PageModel
{
    public void OnGet()
    {
    }
}
