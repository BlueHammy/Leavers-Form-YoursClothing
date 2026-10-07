using System.Security.Claims;
using LeaversForm.Data.Entities;
using LeaversForm.Data.Enums;

namespace LeaversForm.Services;

public interface IScriptEligibilityCalculator
{
    DateTime Calculate(DateOnly leavingDate);
}

public interface IAuditService
{
    Task AddAsync(string action, ClaimsPrincipal? performer, int? leaverRequestId = null,
        string? oldValue = null, string? newValue = null, string? notes = null, CancellationToken cancellationToken = default);
}

public sealed record LeaverRequestListStatus(
    int Id,
    string Status,
    string StatusLabel,
    bool DisableAsap,
    string? StatusDetail,
    bool ShowRetry,
    bool IsActive);

public interface ILeaverRequestStatusPresenter
{
    string? BuildStatusDetail(LeaverRequest request);
    string? BuildFailureGuidance(LeaverRequest request);
}

public interface ILeaverRequestService
{
    Task<LeaverRequest> CreateAsync(LeaverRequest request, IEnumerable<int> managerIds, ClaimsPrincipal performer, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LeaverRequest>> ListAsync(string? search = null, LeaverStatus? status = null, ClaimsPrincipal? viewer = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LeaverRequestListStatus>> ListStatusAsync(string? search = null, ClaimsPrincipal? viewer = null, CancellationToken cancellationToken = default);
    Task<LeaverRequest?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<LeaverRequestListStatus?> GetStatusAsync(int id, ClaimsPrincipal? viewer = null, CancellationToken cancellationToken = default);
    Task UpdateDateAsync(int id, DateOnly leavingDate, ClaimsPrincipal performer, CancellationToken cancellationToken = default);
    Task CancelAsync(int id, ClaimsPrincipal performer, CancellationToken cancellationToken = default);
    Task ApproveAsync(int id, ClaimsPrincipal performer, CancellationToken cancellationToken = default);
    Task RetryAsync(int id, ClaimsPrincipal performer, CancellationToken cancellationToken = default);
}

public interface IGraphUserSearchService
{
    Task<IReadOnlyList<GraphUser>> SearchAsync(string query, CancellationToken cancellationToken = default);
    Task<GraphUser?> GetAsync(string id, CancellationToken cancellationToken = default);
}

public interface IGraphEmailService
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record LeaverFailureNotificationContext(
    string? LeaverDisplayName = null,
    string? LeaverUserPrincipalName = null,
    string? OffboardingLog = null);

public interface IErrorNotificationService
{
    Task NotifyAsync(
        string summary,
        Exception exception,
        int? leaverRequestId = null,
        string? output = null,
        LeaverFailureNotificationContext? leaverContext = null,
        CancellationToken cancellationToken = default);
}

public interface IAuthorisingManagerNotificationService
{
    Task NotifySelectedManagersAsync(LeaverRequest request, IEnumerable<AuthorisingManager> managers, CancellationToken cancellationToken = default);
}

public interface IAdministrativeNotificationService
{
    Task NotifyAsync(
        string subject,
        string title,
        string details,
        ClaimsPrincipal performer,
        CancellationToken cancellationToken = default);
}

public interface IExcelExportService
{
    byte[] Export(IEnumerable<LeaverRequest> requests);
}

public interface IPowerShellScriptExecutor
{
    Task<ScriptExecutionResult> ExecuteAsync(LeaverRequest request, CancellationToken cancellationToken = default);
}

public interface ILeaverScriptProcessor
{
    Task ProcessDueRequestsAsync(CancellationToken cancellationToken = default);
    Task ProcessRequestAsync(int requestId, ClaimsPrincipal? performer = null, CancellationToken cancellationToken = default);
    Task ReconcileStuckRequestsAsync(CancellationToken cancellationToken = default);
    Task ReconcileRequestAsync(int requestId, CancellationToken cancellationToken = default);
}

public interface ILeaversFormProcessingModeService
{
    bool IsScriptTestModeEffective { get; }
    string? LocalSettingsPath { get; }
    Task SetScriptTestModeAsync(bool testMode, ClaimsPrincipal performer, CancellationToken cancellationToken = default);
}

public interface ILeaversFormEmailSettingsService
{
    LeaversEmailSettings Current { get; }
    string ItCcAddress { get; }
    string HrCcAddress { get; }
    string ErrorAlertMailbox { get; }
    string RedirectAllEmailsTo { get; }
    bool RedirectActive { get; }
    bool CcHrOnSubmissionEmails { get; }
    Task UpdateAsync(LeaversEmailSettings settings, ClaimsPrincipal performer, CancellationToken cancellationToken = default);
}

public interface ILeaverRequestLogService
{
    string? GetLatestOffboardingLogPath(int requestId);
    string? GetLatestExecutionLogPath(int requestId);
    string? ReadLatestOffboardingLogText(int requestId);
}

public interface ILeaversOpsService
{
    Task<LeaversOpsConfig> GetConfigAsync(CancellationToken cancellationToken = default);
}

public interface ILeaverExclusionService
{
    Task<IReadOnlyList<LeaverExclusion>> ListActiveAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LeaverExclusion>> ListAllAsync(CancellationToken cancellationToken = default);
    Task<LeaverExclusion> AddAsync(LeaverExclusionMatchType matchType, string matchValue, string? description, CancellationToken cancellationToken = default);
    Task DeactivateAsync(int id, CancellationToken cancellationToken = default);
    Task<LeaverExclusionEvaluation> EvaluateAsync(LeaverExclusionCheck candidate, CancellationToken cancellationToken = default);
}

public interface IAppUserService
{
    Task<AppRole> GetRoleByEmailAsync(string? email, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppUser>> ListElevatedUsersAsync(CancellationToken cancellationToken = default);
    Task<AppUser> AssignRoleAsync(string email, string displayName, AppRole role, CancellationToken cancellationToken = default);
    Task RemoveElevatedAccessAsync(int id, CancellationToken cancellationToken = default);
}
