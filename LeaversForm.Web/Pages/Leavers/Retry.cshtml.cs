using LeaversForm.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LeaversForm.Web.Pages.Leavers;

[Authorize(Policy = "LeaversSuperUser")]
public class RetryModel(ILeaverRequestService requests) : PageModel
{
    public IActionResult OnGet(int id) => RedirectToPage("Details", new { id });

    public async Task<IActionResult> OnPostAsync(int id)
    {
        await requests.RetryAsync(id, User);
        TempData["Success"] = "Disable retry started. The request is now pending.";
        return RedirectToPage("List");
    }
}
