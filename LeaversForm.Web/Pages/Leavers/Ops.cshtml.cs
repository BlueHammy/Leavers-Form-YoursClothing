using LeaversForm.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LeaversForm.Web.Pages.Leavers;

[Authorize(Roles = LeaversRoles.SuperUser)]
public class OpsModel(
    ILeaversOpsService ops,
    ILeaversFormProcessingModeService processingMode,
    ILeaversFormEmailSettingsService emailSettings,
    ILeaverScriptProcessor processor,
    ILeaverScriptRepairService repairService) : PageModel
{
    public LeaversOpsConfig? Config { get; private set; }

    [BindProperty] public string? LiveConfirmation { get; set; }

    [BindProperty] public string? ItCcAddress { get; set; }

    [BindProperty] public string? HrCcAddress { get; set; }

    [BindProperty] public string? ErrorAlertMailbox { get; set; }

    [BindProperty] public string? RedirectAllEmailsTo { get; set; }

    public async Task OnGetAsync()
    {
        Config = await ops.GetConfigAsync();
        BindEmailFields(Config);
    }

    public async Task<IActionResult> OnPostReconcileAllAsync()
    {
        await processor.ReconcileStuckRequestsAsync();
        TempData["Success"] = "Stuck requests fixed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRepairModulesAsync()
    {
        var repaired = await repairService.TryRepairEnvironmentAsync();
        TempData["Success"] = repaired
            ? "Module repair completed."
            : "Module repair ran but reported issues — check application logs.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSwitchToTestAsync()
    {
        await processingMode.SetScriptTestModeAsync(testMode: true, User);
        TempData["Success"] = "Processing switched to TEST — new account disables are stopped.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSwitchToLiveAsync()
    {
        Config = await ops.GetConfigAsync();
        BindEmailFields(Config);

        ModelState.Remove(nameof(ItCcAddress));
        ModelState.Remove(nameof(HrCcAddress));
        ModelState.Remove(nameof(ErrorAlertMailbox));
        ModelState.Remove(nameof(RedirectAllEmailsTo));

        if (!string.Equals(LiveConfirmation?.Trim(), "LIVE", StringComparison.Ordinal))
            ModelState.AddModelError(nameof(LiveConfirmation), "Type LIVE to confirm live processing.");

        if (!ModelState.IsValid)
            return Page();

        await processingMode.SetScriptTestModeAsync(testMode: false, User);
        TempData["Success"] = "Processing switched to LIVE — leaver scripts will run.";
        if (Config.EmailRedirectActive)
        {
            TempData["Warning"] =
                "Email redirect is still on — all mail goes to your test inbox until you clear “Redirect all emails to” and save email settings.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveEmailSettingsAsync()
    {
        Config = await ops.GetConfigAsync();

        if (string.IsNullOrWhiteSpace(ItCcAddress))
            ModelState.AddModelError(nameof(ItCcAddress), "IT CC address is required.");

        if (string.IsNullOrWhiteSpace(HrCcAddress))
            ModelState.AddModelError(nameof(HrCcAddress), "HR CC address is required.");

        if (string.IsNullOrWhiteSpace(ErrorAlertMailbox))
            ModelState.AddModelError(nameof(ErrorAlertMailbox), "Error alert mailbox is required.");

        if (!ModelState.IsValid)
        {
            BindEmailFields(Config);
            return Page();
        }

        try
        {
            await emailSettings.UpdateAsync(
                new LeaversEmailSettings(
                    ItCcAddress!.Trim(),
                    HrCcAddress!.Trim(),
                    ErrorAlertMailbox!.Trim(),
                    RedirectAllEmailsTo?.Trim() ?? string.Empty),
                User);
        }
        catch (ArgumentException ex)
        {
            BindEmailFields(Config);
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        TempData["Success"] = "Email settings saved — new messages will use these addresses immediately.";
        return RedirectToPage();
    }

    private void BindEmailFields(LeaversOpsConfig config)
    {
        ItCcAddress = config.ItCcAddress;
        HrCcAddress = config.HrCcAddress;
        ErrorAlertMailbox = config.ErrorAlertMailbox;
        RedirectAllEmailsTo = config.EmailRedirectTarget ?? string.Empty;
    }
}
