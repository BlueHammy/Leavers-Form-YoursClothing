using LeaversForm.Data.Entities;
using LeaversForm.Data.Enums;
using LeaversForm.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace LeaversForm.Web.Pages.Leavers;

public class DetailsModel(
    ILeaverRequestService requests,
    ILeaverRequestStatusPresenter statusPresenter,
    ILeaverScriptProcessor processor,
    ILeaverScriptExecutionTracker executionTracker,
    ILeaverRequestLogService logService,
    IOptions<LeaversFormOptions> options) : PageModel
{
    public new LeaverRequest? Request { get; private set; }
    public string? StatusDetail { get; private set; }
    public string? FailureGuidance { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanCancel { get; private set; }
    public bool CanRetry { get; private set; }
    public bool CanApprove { get; private set; }
    public bool IsAuthorisingManagerView { get; private set; }
    public bool ShowDiagnostics { get; private set; }
    public bool ShowTechnicalFields { get; private set; }
    public string? PlatformStatusSummary { get; private set; }
    public bool ScriptProcessActive { get; private set; }
    public bool CanReconcile { get; private set; }
    public bool HasScriptTestModeSkipped { get; private set; }
    public string? OffboardingLogPath { get; private set; }
    public string? ExecutionLogPath { get; private set; }
    public IReadOnlyList<AuditLog> AuditTimeline { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Request = await requests.GetAsync(id);
        if (Request is null)
            return NotFound();

        if (Request.Status == LeaverStatus.Cancelled)
            return RedirectToPage("List");

        if (!await LoadAsync(id))
            return Forbid();

        return Page();
    }

    public async Task<IActionResult> OnGetDownloadLogAsync(int id, string type)
    {
        if (!LeaversRoles.IsSuperUser(User))
            return Forbid();

        Request = await requests.GetAsync(id);
        if (Request is null || !LeaversRoles.CanViewRequest(User, Request))
            return NotFound();

        var logFolder = options.Value.LogFolderPath;
        string? filePath = type?.ToLowerInvariant() switch
        {
            "offboarding" => logService.GetLatestOffboardingLogPath(id),
            "summary" => logService.GetLatestExecutionLogPath(id) ?? Request.ScriptExecutionLogPath,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
            return NotFound();

        if (!LeaverRequestLogService.IsPathWithinFolder(filePath, logFolder))
            return Forbid();

        var downloadName = Path.GetFileName(filePath);
        return PhysicalFile(filePath, "text/plain", downloadName);
    }

    public async Task<IActionResult> OnPostReconcileAsync(int id)
    {
        if (!LeaversRoles.IsSuperUser(User))
            return Forbid();

        Request = await requests.GetAsync(id);
        if (Request is null || !LeaversRoles.CanViewRequest(User, Request))
            return NotFound();

        await processor.ReconcileRequestAsync(id);
        TempData["Success"] = "Request updated — refresh to see the latest status.";
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(int id)
    {
        if (Request is null)
            Request = await requests.GetAsync(id);

        if (Request is null)
            return false;

        if (!LeaversRoles.CanViewRequest(User, Request))
            return false;

        StatusDetail = statusPresenter.BuildStatusDetail(Request);
        FailureGuidance = statusPresenter.BuildFailureGuidance(Request);
        CanEdit = LeaversRoles.CanEditRequest(User, Request);
        CanCancel = LeaversRoles.CanCancelRequest(User, Request);
        CanRetry = LeaversRoles.CanRetryRequest(User, Request);
        CanApprove = LeaversRoles.CanApproveRequest(User, Request);
        IsAuthorisingManagerView = !LeaversRoles.CanManageAllRequests(User)
            && !LeaversRoles.NormalizeEmail(LeaversRoles.GetEmail(User) ?? string.Empty).Equals(
                LeaversRoles.NormalizeEmail(Request.SubmittedByEmail),
                StringComparison.Ordinal)
            && LeaversRoles.CanViewRequest(User, Request);

        ShowDiagnostics = LeaversRoles.IsSuperUser(User);
        ShowTechnicalFields = LeaversRoles.IsAdminOrAbove(User);
        if (ShowDiagnostics)
        {
            var offboardingLog = logService.ReadLatestOffboardingLogText(id);
            PlatformStatusSummary = LeaverScriptPlatformStatus.BuildPlainTextSummary(offboardingLog);
            ScriptProcessActive = executionTracker.IsRunning(id);
            CanReconcile = Request.Status is LeaverStatus.Pending
                || (Request.Status == LeaverStatus.Scheduled && Request.ScriptLastAttemptAt.HasValue);
            HasScriptTestModeSkipped = Request.AuditLogs.Any(x => x.Action == "ScriptTestModeSkipped");
            OffboardingLogPath = logService.GetLatestOffboardingLogPath(id);
            ExecutionLogPath = logService.GetLatestExecutionLogPath(id) ?? Request.ScriptExecutionLogPath;
            AuditTimeline = Request.AuditLogs.OrderByDescending(x => x.PerformedAt).ToList();
        }

        return true;
    }
}
