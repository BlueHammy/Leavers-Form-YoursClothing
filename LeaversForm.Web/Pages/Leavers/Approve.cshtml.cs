using LeaversForm.Data.Enums;
using LeaversForm.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LeaversForm.Web.Pages.Leavers;

public class ApproveModel(ILeaverRequestService requests) : PageModel
{
    public new LeaversForm.Data.Entities.LeaverRequest? Request { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Request = await requests.GetAsync(id);
        if (Request is null)
            return NotFound();

        if (Request.Status == LeaverStatus.Cancelled)
            return RedirectToPage("List");

        if (!LeaversRoles.CanApproveRequest(User, Request))
            return Forbid();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        Request = await requests.GetAsync(id);
        if (Request is null)
            return NotFound();

        if (!LeaversRoles.CanApproveRequest(User, Request))
            return Forbid();

        await requests.ApproveAsync(id, User);
        TempData["Success"] = $"Approved — {Request.LeaverDisplayName}'s account will be switched off as scheduled.";
        return RedirectToPage("Details", new { id });
    }
}
