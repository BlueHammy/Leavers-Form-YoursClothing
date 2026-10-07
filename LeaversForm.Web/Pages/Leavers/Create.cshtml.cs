using System.ComponentModel.DataAnnotations;
using LeaversForm.Data;
using LeaversForm.Data.Entities;
using LeaversForm.Data.Enums;
using LeaversForm.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LeaversForm.Web.Pages.Leavers;

public class CreateModel(
    ILeaverRequestService requests,
    LeaversFormDbContext db,
    ILeaverExclusionService exclusions) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public IReadOnlyList<AuthorisingManager> Managers { get; private set; } = [];
    public bool CanDisableAsap { get; private set; }
    public sealed class InputModel
    {
        [Display(Name = "Leaver")]
        public string? LeaverSearchText { get; set; }

        public string? LeaverEntraId { get; set; }
        public string? LeaverDisplayName { get; set; }
        public string? LeaverUserPrincipalName { get; set; }
        public string? LeaverEmail { get; set; }
        public string? Department { get; set; }
        public string? SamAccountName { get; set; }

        [Required(ErrorMessage = "Choose a leaving date.")]
        [Display(Name = "Leaving date")]
        [DataType(DataType.Date)]
        public DateOnly? LeavingDate { get; set; }

        public string[] EquipmentItems { get; set; } = [];
        public string? AnythingElse { get; set; }

        public int[] ManagerIds { get; set; } = [];

        [Display(Name = "Are there any special requests we should know about?")]
        public string? SpecialRequests { get; set; }

        public bool DisableAsap { get; set; }
    }

    public static readonly string[] EquipmentOptions =
    [
        "None", "Laptop", "iPad", "Work Phone", "Work from home kit"
    ];

    public async Task OnGetAsync()
    {
        await LoadManagersAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadManagersAsync();
        ClearLeaverFieldValidationState();
        ValidateLeaver();
        await ValidateLeaverExclusionAsync();
        ValidateEquipment();
        ValidateManagers();

        if (!ModelState.IsValid)
            return Page();

        var disableAsap = CanDisableAsap && Input.DisableAsap;

        try
        {
            var request = await requests.CreateAsync(new LeaverRequest
            {
                LeaverEntraId = Input.LeaverEntraId!,
                LeaverDisplayName = Input.LeaverDisplayName!,
                LeaverUserPrincipalName = Input.LeaverUserPrincipalName!,
                LeaverEmail = string.IsNullOrWhiteSpace(Input.LeaverEmail) ? Input.LeaverUserPrincipalName! : Input.LeaverEmail,
                Department = Input.Department,
                SamAccountName = Input.SamAccountName,
                LeavingDate = Input.LeavingDate!.Value,
                EquipmentNotes = BuildEquipmentNotes(),
                SpecialRequests = string.IsNullOrWhiteSpace(Input.SpecialRequests) ? null : Input.SpecialRequests.Trim(),
                DisableAsap = disableAsap
            }, Input.ManagerIds, User);

            TempData["Success"] = request.Status == LeaverStatus.AwaitingApproval
                ? $"Leaver request submitted for {request.LeaverDisplayName}. It must be approved before their account can be switched off."
                : $"Leaver request submitted for {request.LeaverDisplayName}.";
            TempData["HighlightRequestId"] = request.Id;
            return RedirectToPage("List", new { highlight = request.Id });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("Input.LeaverSearchText", ex.Message);
            return Page();
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    private async Task ValidateLeaverExclusionAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.LeaverDisplayName)
            && string.IsNullOrWhiteSpace(Input.LeaverUserPrincipalName)
            && string.IsNullOrWhiteSpace(Input.LeaverEmail))
            return;

        var evaluation = await exclusions.EvaluateAsync(new LeaverExclusionCheck(
            Input.LeaverDisplayName,
            string.IsNullOrWhiteSpace(Input.LeaverEmail) ? Input.LeaverUserPrincipalName : Input.LeaverEmail,
            Input.LeaverUserPrincipalName,
            Input.SamAccountName));

        if (evaluation.IsExcluded)
            ModelState.AddModelError("Input.LeaverSearchText", "This account cannot be submitted as a leaver due to a security exclusion.");
    }

    private void ValidateLeaver()
    {
        if (string.IsNullOrWhiteSpace(Input.LeaverEntraId))
            ModelState.AddModelError("Input.LeaverSearchText", "Select a leaver from the search results.");
    }

    private void ClearLeaverFieldValidationState()
    {
        foreach (var key in new[]
        {
            "Input.LeaverSearchText", "Input.LeaverEntraId", "Input.LeaverDisplayName",
            "Input.LeaverUserPrincipalName", "Input.LeaverEmail", "Input.Department"
        })
            ModelState.Remove(key);
    }

    private void ValidateEquipment()
    {
        var selected = Input.EquipmentItems.Where(x => EquipmentOptions.Contains(x)).Distinct().ToArray();
        if (selected.Contains("None"))
            selected = ["None"];
        Input.EquipmentItems = selected;
        var anythingElse = Input.AnythingElse?.Trim();

        if (selected.Length == 0 && string.IsNullOrWhiteSpace(anythingElse))
            ModelState.AddModelError("Input.EquipmentItems", "Select at least one item to be returned, choose None, or explain in Anything else.");
    }

    private string BuildEquipmentNotes()
    {
        var selected = Input.EquipmentItems.Distinct().ToArray();
        if (selected.Contains("None"))
            selected = ["None"];

        var lines = new List<string>
        {
            selected.Length > 0
                ? selected.Contains("None")
                    ? "Returning: None"
                    : $"Returning: {string.Join(", ", selected)}"
                : "Returning: None selected"
        };

        if (!string.IsNullOrWhiteSpace(Input.AnythingElse))
            lines.Add($"Anything else: {Input.AnythingElse.Trim()}");

        return string.Join(Environment.NewLine, lines);
    }

    private void ValidateManagers()
    {
        if (Input.ManagerIds.Length == 0)
            ModelState.AddModelError("Input.ManagerIds", "Select at least one authorising manager.");
    }

    private async Task LoadManagersAsync()
    {
        CanDisableAsap = LeaversRoles.IsAdminOrAbove(User);
        Managers = await db.AuthorisingManagers.Where(x => x.IsActive).OrderBy(x => x.DisplayName).ToListAsync();
    }
}
