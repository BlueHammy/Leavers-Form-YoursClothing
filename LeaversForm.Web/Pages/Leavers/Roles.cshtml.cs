using LeaversForm.Data.Enums;
using LeaversForm.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LeaversForm.Web.Pages.Leavers;

[Authorize(Roles = LeaversRoles.SuperUser)]
public class RolesModel(
    IAppUserService appUsers,
    IAdministrativeNotificationService notifications) : PageModel
{
    public IReadOnlyList<Data.Entities.AppUser> Users { get; private set; } = [];

    [BindProperty] public AssignRoleInput Input { get; set; } = new();

    public sealed class AssignRoleInput
    {
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public AppRole Role { get; set; } = AppRole.Admin;
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAssignAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.Email))
            ModelState.AddModelError("Input.Email", "Enter an email address.");
        if (string.IsNullOrWhiteSpace(Input.DisplayName))
            ModelState.AddModelError("Input.DisplayName", "Enter a display name.");
        if (Input.Role == AppRole.User)
            ModelState.AddModelError("Input.Role", "Choose super user or admin.");

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        try
        {
            var role = await appUsers.AssignRoleAsync(Input.Email, Input.DisplayName, Input.Role);
            await notifications.NotifyAsync(
                $"Leavers Form role changed: {role.DisplayName}",
                "User role changed",
                $"""
                <p><strong>Action:</strong> Role assigned or updated</p>
                <p><strong>Account:</strong> {EmailTemplate.Encode(role.DisplayName)} &lt;{EmailTemplate.Encode(role.Email)}&gt;</p>
                <p><strong>New role:</strong> {EmailTemplate.Encode(role.Role.ToString())}</p>
                """,
                User);
            TempData["Success"] = "Role assigned.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id)
    {
        try
        {
            var existing = (await appUsers.ListElevatedUsersAsync()).SingleOrDefault(x => x.Id == id);
            await appUsers.RemoveElevatedAccessAsync(id);
            if (existing is not null)
            {
                await notifications.NotifyAsync(
                    $"Leavers Form role removed: {existing.DisplayName}",
                    "Elevated access removed",
                    $"""
                    <p><strong>Action:</strong> User returned to standard access</p>
                    <p><strong>Account:</strong> {EmailTemplate.Encode(existing.DisplayName)} &lt;{EmailTemplate.Encode(existing.Email)}&gt;</p>
                    <p><strong>Previous role:</strong> {EmailTemplate.Encode(existing.Role.ToString())}</p>
                    """,
                    User);
            }
            TempData["Success"] = "User returned to normal access.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            TempData["Success"] = null;
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadAsync();
            return Page();
        }
    }

    private async Task LoadAsync() => Users = await appUsers.ListElevatedUsersAsync();
}
