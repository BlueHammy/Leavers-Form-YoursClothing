using LeaversForm.Data.Enums;

using LeaversForm.Services;

using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Mvc.RazorPages;



namespace LeaversForm.Web.Pages.Leavers;



[Authorize(Policy = "LeaversAdmin")]

public class CancelModel(ILeaverRequestService requests) : PageModel

{

    public new LeaversForm.Data.Entities.LeaverRequest? Request { get; private set; }



    public async Task<IActionResult> OnGetAsync(int id)

    {

        Request = await requests.GetAsync(id);

        if (Request is null)

            return NotFound();

        if (Request.Status == LeaverStatus.Cancelled)

            return RedirectToPage("List");

        if (!LeaversRoles.CanCancelRequest(User, Request))

            return Forbid();



        return Page();

    }



    public async Task<IActionResult> OnPostAsync(int id)

    {

        await requests.CancelAsync(id, User);

        return RedirectToPage("List");

    }

}

