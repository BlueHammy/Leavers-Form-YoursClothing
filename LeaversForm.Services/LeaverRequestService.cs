using System.Security.Claims;
using LeaversForm.Data;
using LeaversForm.Data.Entities;
using LeaversForm.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeaversForm.Services;

public sealed class LeaverRequestService(
    LeaversFormDbContext db,
    IScriptEligibilityCalculator eligibility,
    IAuditService audit,
    IAsapLeaverScriptDispatcher asapDispatcher,
    ILeaverExclusionService exclusions,
    IAuthorisingManagerNotificationService managerNotifications,
    ILeaverRequestStatusPresenter statusPresenter,
    ILeaverScriptExecutionTracker executionTracker,
    IOptions<LeaversFormOptions> options) : ILeaverRequestService
{
    private static readonly TimeZoneInfo UkTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "GMT Standard Time" : "Europe/London");

    private static readonly LeaverStatus[] DuplicateBlockingStatuses =
    [
        LeaverStatus.Pending,
        LeaverStatus.Scheduled,
        LeaverStatus.Failed,
        LeaverStatus.Completed,
        LeaverStatus.AwaitingApproval
    ];

    private static readonly LeaverStatus[] ActiveStatuses =
    [
        LeaverStatus.Scheduled,
        LeaverStatus.Pending,
        LeaverStatus.AwaitingApproval
    ];

    public async Task<LeaverRequest> CreateAsync(LeaverRequest request, IEnumerable<int> managerIds, ClaimsPrincipal performer, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.LeaverEntraId)) throw new ArgumentException("Select a leaver.");
        if (string.IsNullOrWhiteSpace(request.EquipmentNotes)) throw new ArgumentException("Equipment to be returned is required.");
        if (request.DisableAsap && !LeaversRoles.IsAdminOrAbove(performer))
            request.DisableAsap = false;
        var ids = managerIds.Distinct().ToArray();
        if (ids.Length == 0) throw new ArgumentException("Select at least one authorising manager.");

        var normalizedUpn = request.LeaverUserPrincipalName.Trim().ToLowerInvariant();
        if (await db.LeaverRequests.AnyAsync(
                x => DuplicateBlockingStatuses.Contains(x.Status)
                    && (x.LeaverEntraId == request.LeaverEntraId
                        || x.LeaverUserPrincipalName.ToLower() == normalizedUpn),
                cancellationToken))
        {
            throw new InvalidOperationException(
                "This user has already been submitted. Please check the request list to ensure everything is correct.");
        }

        var exclusion = await exclusions.EvaluateAsync(new LeaverExclusionCheck(
            request.LeaverDisplayName,
            request.LeaverEmail,
            request.LeaverUserPrincipalName,
            request.SamAccountName), cancellationToken);
        if (exclusion.IsExcluded)
            throw new InvalidOperationException("This account cannot be submitted as a leaver due to a security exclusion.");

        var name = performer.Identity?.Name ?? "Unknown";
        request.SubmittedByName = name;
        var performerEmail = performer.FindFirst(ClaimTypes.Email)?.Value
            ?? performer.FindFirst("preferred_username")?.Value
            ?? string.Empty;
        request.SubmittedByEmail = string.IsNullOrWhiteSpace(performerEmail)
            ? string.Empty
            : LeaversRoles.NormalizeEmail(performerEmail);
        request.SubmittedAt = DateTime.UtcNow;

        var requiresApproval = !LeaversRoles.IsAdminOrAbove(performer);
        if (requiresApproval)
            await EnforceDailySubmissionLimitAsync(request.SubmittedByEmail, cancellationToken);

        request.ScriptExecutionStatus = ScriptExecutionStatus.NotRun;
        request.AuthorisingManagers = ids.Select(id => new LeaverRequestAuthorisingManager { AuthorisingManagerId = id }).ToList();

        if (requiresApproval)
        {
            request.Status = LeaverStatus.AwaitingApproval;
            request.ScriptEligibleAt = DateTime.MaxValue;
        }
        else
        {
            request.Status = LeaverStatus.Scheduled;
            request.ScriptEligibleAt = CalculateScriptEligibleAt(request);
            request.ApprovedAt = DateTime.UtcNow;
            request.ApprovedByName = name;
            request.ApprovedByEmail = request.SubmittedByEmail;
        }

        db.LeaverRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        await audit.AddAsync("Created", performer, request.Id, notes: requiresApproval
            ? "Leaver request submitted — awaiting approval"
            : "Leaver request submitted", cancellationToken: cancellationToken);

        var selectedManagers = await db.AuthorisingManagers
            .AsNoTracking()
            .Where(manager => ids.Contains(manager.Id) && manager.IsActive)
            .ToListAsync(cancellationToken);
        await managerNotifications.NotifySelectedManagersAsync(request, selectedManagers, cancellationToken);

        if (!requiresApproval && request.DisableAsap)
            asapDispatcher.Enqueue(request.Id);

        return request;
    }

    public async Task ApproveAsync(int id, ClaimsPrincipal performer, CancellationToken cancellationToken = default)
    {
        var request = await db.LeaverRequests
            .Include(x => x.AuthorisingManagers)
            .ThenInclude(x => x.AuthorisingManager)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException();

        if (!LeaversRoles.CanApproveRequest(performer, request))
            throw new UnauthorizedAccessException("You do not have permission to approve this request.");

        if (request.Status != LeaverStatus.AwaitingApproval)
            throw new InvalidOperationException("This request is not waiting for approval.");

        var approverEmail = performer.FindFirst(ClaimTypes.Email)?.Value
            ?? performer.FindFirst("preferred_username")?.Value
            ?? string.Empty;

        request.Status = LeaverStatus.Scheduled;
        request.ScriptEligibleAt = CalculateScriptEligibleAt(request);
        request.ApprovedAt = DateTime.UtcNow;
        request.ApprovedByName = performer.Identity?.Name;
        request.ApprovedByEmail = string.IsNullOrWhiteSpace(approverEmail)
            ? null
            : LeaversRoles.NormalizeEmail(approverEmail);
        await db.SaveChangesAsync(cancellationToken);
        await audit.AddAsync("Approved", performer, id, notes: "Request approved — account switch-off scheduled", cancellationToken: cancellationToken);

        if (request.DisableAsap)
            asapDispatcher.Enqueue(id);
    }

    private async Task EnforceDailySubmissionLimitAsync(string submitterEmail, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(submitterEmail))
            return;

        var limit = options.Value.MaxSubmissionsPerDayForUsers;
        if (limit <= 0)
            return;

        var (dayStartUtc, dayEndUtc) = GetCurrentUkDayBoundsUtc();
        var normalized = LeaversRoles.NormalizeEmail(submitterEmail);
        var count = await db.LeaverRequests.CountAsync(
            x => x.SubmittedByEmail.ToLower() == normalized
                && x.SubmittedAt >= dayStartUtc
                && x.SubmittedAt < dayEndUtc
                && x.Status != LeaverStatus.Cancelled,
            cancellationToken);

        if (count >= limit)
            throw new InvalidOperationException(
                $"You can submit up to {limit} leaver requests per day. Please try again tomorrow or contact IT if you need help.");
    }

    private static (DateTime startUtc, DateTime endUtc) GetCurrentUkDayBoundsUtc()
    {
        var ukNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, UkTimeZone);
        var ukStart = ukNow.Date;
        var ukEnd = ukStart.AddDays(1);
        return (
            TimeZoneInfo.ConvertTimeToUtc(ukStart, UkTimeZone),
            TimeZoneInfo.ConvertTimeToUtc(ukEnd, UkTimeZone));
    }

    private DateTime CalculateScriptEligibleAt(LeaverRequest request) =>
        request.DisableAsap ? DateTime.UtcNow : eligibility.Calculate(request.LeavingDate);

    public async Task<IReadOnlyList<LeaverRequest>> ListAsync(string? search = null, LeaverStatus? status = null, ClaimsPrincipal? viewer = null, CancellationToken cancellationToken = default)
    {
        var query = db.LeaverRequests.AsNoTracking().Include(x => x.AuthorisingManagers).ThenInclude(x => x.AuthorisingManager).AsQueryable();
        if (viewer is not null && !LeaversRoles.CanManageAllRequests(viewer))
        {
            var email = LeaversRoles.GetEmail(viewer);
            if (string.IsNullOrWhiteSpace(email))
                return [];

            var normalized = LeaversRoles.NormalizeEmail(email);
            query = query.Where(x =>
                x.SubmittedByEmail.ToLower() == normalized
                || x.AuthorisingManagers.Any(link =>
                    link.AuthorisingManager != null
                    && link.AuthorisingManager.Email.ToLower() == normalized));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var statusMatches = Enum.GetValues<LeaverStatus>()
                .Where(value => value.ToString().Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var pattern = $"%{search.Trim()}%";
            query = statusMatches.Length > 0
                ? query.Where(x =>
                    EF.Functions.Like(x.LeaverDisplayName, pattern) ||
                    (x.Department != null && EF.Functions.Like(x.Department, pattern)) ||
                    statusMatches.Contains(x.Status))
                : query.Where(x =>
                    EF.Functions.Like(x.LeaverDisplayName, pattern) ||
                    (x.Department != null && EF.Functions.Like(x.Department, pattern)));
        }
        if (status.HasValue) query = query.Where(x => x.Status == status);
        return await query.OrderByDescending(x => ActiveStatuses.Contains(x.Status)).ThenBy(x => x.LeavingDate).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LeaverRequestListStatus>> ListStatusAsync(
        string? search = null,
        ClaimsPrincipal? viewer = null,
        CancellationToken cancellationToken = default)
    {
        var requests = await ListAsync(search, viewer: viewer, cancellationToken: cancellationToken);
        return requests.Select(request => new LeaverRequestListStatus(
            request.Id,
            request.Status.ToString(),
            LeaverStatusDisplay.ToLabel(request.Status),
            request.DisableAsap,
            statusPresenter.BuildStatusDetail(request),
            viewer is not null && LeaversRoles.CanRetryRequest(viewer, request),
            request.Status is LeaverStatus.Pending or LeaverStatus.Scheduled or LeaverStatus.AwaitingApproval)).ToList();
    }

    public Task<LeaverRequest?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        db.LeaverRequests.Include(x => x.AuthorisingManagers).ThenInclude(x => x.AuthorisingManager).Include(x => x.AuditLogs).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<LeaverRequestListStatus?> GetStatusAsync(int id, ClaimsPrincipal? viewer = null, CancellationToken cancellationToken = default)
    {
        var request = await GetAsync(id, cancellationToken);
        if (request is null || viewer is not null && !LeaversRoles.CanViewRequest(viewer, request))
            return null;

        return new LeaverRequestListStatus(
            request.Id,
            request.Status.ToString(),
            LeaverStatusDisplay.ToLabel(request.Status),
            request.DisableAsap,
            statusPresenter.BuildStatusDetail(request),
            viewer is not null && LeaversRoles.CanRetryRequest(viewer, request),
            request.Status is LeaverStatus.Pending or LeaverStatus.Scheduled or LeaverStatus.AwaitingApproval);
    }

    public async Task UpdateDateAsync(int id, DateOnly leavingDate, ClaimsPrincipal performer, CancellationToken cancellationToken = default)
    {
        if (!LeaversRoles.CanEditRequest(performer, await RequireRequestAsync(id, cancellationToken)))
            throw new UnauthorizedAccessException("You do not have permission to edit leaver requests.");

        var request = await db.LeaverRequests.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException();
        if (request.Status == LeaverStatus.Cancelled)
            throw new InvalidOperationException("Cancelled requests cannot be edited.");
        if (request.Status == LeaverStatus.Completed && !LeaversRoles.IsAdminOrAbove(performer)) throw new InvalidOperationException("Completed requests require administrator access.");
        var old = request.LeavingDate.ToString("yyyy-MM-dd");
        request.LeavingDate = leavingDate;
        if (!request.DisableAsap)
            request.ScriptEligibleAt = eligibility.Calculate(leavingDate);
        request.LastUpdatedAt = DateTime.UtcNow;
        request.LastUpdatedByName = performer.Identity?.Name;
        await db.SaveChangesAsync(cancellationToken);
        await audit.AddAsync("Updated", performer, id, old, leavingDate.ToString("yyyy-MM-dd"), cancellationToken: cancellationToken);
    }

    public async Task CancelAsync(int id, ClaimsPrincipal performer, CancellationToken cancellationToken = default)
    {
        var request = await RequireRequestAsync(id, cancellationToken);
        if (!LeaversRoles.CanCancelRequest(performer, request))
            throw new UnauthorizedAccessException("You do not have permission to cancel leaver requests.");

        if (request.Status == LeaverStatus.Cancelled)
            throw new InvalidOperationException("This request is already cancelled.");

        var wasPending = request.Status == LeaverStatus.Pending;
        var terminateAttempted = false;
        if (wasPending)
            terminateAttempted = executionTracker.TryTerminate(id);

        request.Status = LeaverStatus.Cancelled;
        request.CancelledAt = DateTime.UtcNow;
        request.CancelledByName = performer.Identity?.Name;
        request.CancelledByEmail = performer.FindFirst(ClaimTypes.Email)?.Value;
        await db.SaveChangesAsync(cancellationToken);

        string? cancelNotes = wasPending
            ? terminateAttempted
                ? "Cancelled (script terminate attempted)"
                : "Cancelled while pending (no active script process was found to stop)"
            : null;
        await audit.AddAsync("Cancelled", performer, id, notes: cancelNotes, cancellationToken: cancellationToken);
    }

    public async Task RetryAsync(int id, ClaimsPrincipal performer, CancellationToken cancellationToken = default)
    {
        var request = await RequireRequestAsync(id, cancellationToken);
        if (!LeaversRoles.CanRetryRequest(performer, request))
            throw new UnauthorizedAccessException("Only super users can retry failed leaver requests.");

        if (request.Status != LeaverStatus.Failed) throw new InvalidOperationException("Only failed requests can be retried.");

        request.Status = LeaverStatus.Scheduled;
        request.ScriptExecutionStatus = ScriptExecutionStatus.NotRun;
        request.ScriptExecutedAt = null;
        request.ScriptExecutionLogPath = null;
        request.ScriptLastAttemptAt = null;
        request.ScriptLastFailedAt = null;
        request.ScriptAutoRetryCount = 0;
        request.ScriptEligibleAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await audit.AddAsync("RetryRequested", performer, id, cancellationToken: cancellationToken);

        if (request.DisableAsap)
            asapDispatcher.Enqueue(id);
    }

    private async Task<LeaverRequest> RequireRequestAsync(int id, CancellationToken cancellationToken = default) =>
        await db.LeaverRequests.FindAsync([id], cancellationToken) ?? throw new KeyNotFoundException();
}
