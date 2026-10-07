using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LeaversForm.Web.Pages;

public class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Leavers/List");
}
