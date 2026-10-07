using LeaversForm.Data.Enums;
using LeaversForm.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LeaversForm.Web.Pages.Leavers;

[Authorize(Roles = LeaversRoles.SuperUser)]
public class ExclusionsModel(
    ILeaverExclusionService exclusions,
    IAdministrativeNotificationService notifications) : PageModel
{
    public IReadOnlyList<Data.Entities.LeaverExclusion> Items { get; private set; } = [];

    [BindProperty] public AddExclusionInput Input { get; set; } = new();

    public sealed class AddExclusionInput
    {
        public LeaverExclusionMatchType MatchType { get; set; } = LeaverExclusionMatchType.DisplayNameExact;
        public string MatchValue { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public static string DescribeMatchType(LeaverExclusionMatchType matchType) => matchType switch
    {
        LeaverExclusionMatchType.AccountPrefix => "Account prefix",
        LeaverExclusionMatchType.DisplayNameExact => "Exact display name",
        LeaverExclusionMatchType.EmailExact => "Exact email or UPN",
        LeaverExclusionMatchType.TextContains => "Contains text",
        _ => matchType.ToString()
    };

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAddAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.MatchValue))
            ModelState.AddModelError("Input.MatchValue", "Enter a value.");

        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        try
        {
            var exclusion = await exclusions.AddAsync(Input.MatchType, Input.MatchValue, Input.Description);
            await notifications.NotifyAsync(
                "Leavers Form exclusion added",
                "Security exclusion added",
                $"""
                <p><strong>Action:</strong> New exclusion added</p>
                <p><strong>Match type:</strong> {EmailTemplate.Encode(DescribeMatchType(exclusion.MatchType))}</p>
                <p><strong>Match value:</strong> {EmailTemplate.Encode(exclusion.MatchValue)}</p>
                <p><strong>Description:</strong> {EmailTemplate.Encode(exclusion.Description ?? "None")}</p>
                """,
                User);
            TempData["Success"] = "Exclusion added.";
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
        var existing = (await exclusions.ListAllAsync()).SingleOrDefault(x => x.Id == id);
        await exclusions.DeactivateAsync(id);
        if (existing is not null)
        {
            await notifications.NotifyAsync(
                "Leavers Form exclusion removed",
                "Security exclusion removed",
                $"""
                <p><strong>Action:</strong> Exclusion deactivated</p>
                <p><strong>Match type:</strong> {EmailTemplate.Encode(DescribeMatchType(existing.MatchType))}</p>
                <p><strong>Match value:</strong> {EmailTemplate.Encode(existing.MatchValue)}</p>
                <p><strong>Description:</strong> {EmailTemplate.Encode(existing.Description ?? "None")}</p>
                """,
                User);
        }
        TempData["Success"] = "Exclusion removed.";
        return RedirectToPage();
    }

    private async Task LoadAsync() => Items = await exclusions.ListActiveAsync();
}
