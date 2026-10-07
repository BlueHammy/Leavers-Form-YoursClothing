using LeaversForm.Data.Enums;

using LeaversForm.Services;

using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Mvc.RazorPages;



namespace LeaversForm.Web.Pages.Leavers;



[Authorize(Policy = "LeaversAdmin")]

public class EditModel(ILeaverRequestService requests) : PageModel

{

    [BindProperty] public DateOnly LeavingDate { get; set; }

    public new LeaversForm.Data.Entities.LeaverRequest? Request { get; private set; }



    public async Task<IActionResult> OnGetAsync(int id)

    {

        Request = await requests.GetAsync(id);

        if (Request is null)

            return NotFound();

        if (Request.Status == LeaverStatus.Cancelled)

            return RedirectToPage("List");

        if (!LeaversRoles.CanEditRequest(User, Request))

            return Forbid();



        LeavingDate = Request.LeavingDate;

        return Page();

    }



    public async Task<IActionResult> OnPostAsync(int id)

    {

        try

        {

            await requests.UpdateDateAsync(id, LeavingDate, User);

            return RedirectToPage("List");

        }

        catch (Exception ex)

        {

            ModelState.AddModelError(string.Empty, ex.Message);

            return await OnGetAsync(id) is IActionResult result ? result : Page();

        }

    }

}

