using System.Security.Claims;
using System.Text.RegularExpressions;
using LeaversForm.Data;
using LeaversForm.Data.Entities;
using LeaversForm.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeaversForm.Services;

public sealed class LeaverScriptProcessor(
    LeaversFormDbContext db,
    IPowerShellScriptExecutor executor,
    IAuditService audit,
    IErrorNotificationService errors,
    ILeaverScriptExecutionTracker executionTracker,
    ILeaverScriptRepairService repairService,
    IAsapLeaverScriptDispatcher asapDispatcher,
    ILeaversFormProcessingModeService processingMode,
    IOptions<LeaversFormOptions> options,
    ILogger<LeaverScriptProcessor> logger) : ILeaverScriptProcessor
{
    private static readonly LeaverStatus[] RunnableStatuses = [LeaverStatus.Scheduled];
    private static readonly Regex FailureCountPattern = new(@"Offboarding completed with (\d+) failed action\(s\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task ProcessDueRequestsAsync(CancellationToken cancellationToken = default)
    {
        await ReconcileStuckRequestsAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var dueIds = await db.LeaverRequests
            .AsNoTracking()
            .Where(x => x.Status == LeaverStatus.Scheduled
                && x.ScriptExecutedAt == null
                && x.ScriptEligibleAt <= now)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in dueIds)
            await ProcessRequestAsync(id, cancellationToken: cancellationToken);
    }

    public async Task ProcessRequestAsync(int requestId, ClaimsPrincipal? performer = null, CancellationToken cancellationToken = default)
    {
        var request = await db.LeaverRequests.SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken)
            ?? throw new KeyNotFoundException($"Leaver request {requestId} was not found.");

        if (request.Status is LeaverStatus.Cancelled or LeaverStatus.Completed)
            return;

        if (!RunnableStatuses.Contains(request.Status) || request.ScriptExecutedAt != null)
            return;

        if (request.ScriptEligibleAt > DateTime.UtcNow)
            return;

        if (request.Status == LeaverStatus.Pending)
        {
            if (executionTracker.IsRunning(requestId))
            {
                logger.LogInformation("Request {RequestId} already has an active script process", requestId);
                return;
            }

            logger.LogWarning(
                "Request {RequestId} is pending without an active script process — reconciliation will fail or complete it",
                requestId);
            return;
        }

        if (!executionTracker.TryBegin(requestId))
        {
            logger.LogInformation("Request {RequestId} already has an active script process", requestId);
            return;
        }

        request.Status = LeaverStatus.Pending;
        request.ScriptLastAttemptAt = DateTime.UtcNow;
        request.ScriptLastFailedAt = null;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await audit.AddAsync("ScriptStarted", performer, request.Id, cancellationToken: cancellationToken);
            await ExecuteWithAutoRecoveryAsync(request, performer, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Leaver processing cancelled for request {RequestId} during shutdown", requestId);
            await MarkFailedAsync(requestId, "Processing was interrupted during application shutdown.", performer, notify: true, cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Leaver processing failed for request {RequestId}", requestId);
            await MarkFailedAsync(requestId, ex.Message, performer, notify: true, cancellationToken);
        }
        finally
        {
            executionTracker.End(requestId);

            var reloaded = await db.LeaverRequests.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);
            if (reloaded is { Status: LeaverStatus.Pending, ScriptExecutedAt: null })
            {
                logger.LogWarning(
                    "Request {RequestId} still pending after script execution — running immediate reconciliation",
                    requestId);
                await ReconcileRequestAsync(requestId, cancellationToken);
            }
        }
    }

    public async Task ReconcileRequestAsync(int requestId, CancellationToken cancellationToken = default)
    {
        executionTracker.TryTerminate(requestId);
        executionTracker.End(requestId);

        var request = await db.LeaverRequests.SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);
        if (request is null || request.Status is not LeaverStatus.Pending || request.ScriptExecutedAt != null)
            return;

        var logText = ReadLatestOffboardingLogText(requestId);
        if (LeaverScriptFailureClassifier.ScriptFinished(logText))
        {
            var exitCode = logText!.Contains("Offboarding failed because", StringComparison.OrdinalIgnoreCase)
                || logText.Contains("Offboarding completed with", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
            logger.LogInformation("Reconciling request {RequestId} from completed offboarding log", requestId);
            await ApplyScriptResultAsync(request, exitCode, executorNotes: null, performer: null, cancellationToken);
            return;
        }

        if (LeaverScriptFailureClassifier.CoreDisableCompleted(logText))
        {
            logger.LogInformation("Reconciling request {RequestId}: core disable completed in log", requestId);
            await ApplyScriptResultAsync(request, 0, executorNotes: null, performer: null, cancellationToken);
        }
    }

    private async Task ExecuteWithAutoRecoveryAsync(
        LeaverRequest request,
        ClaimsPrincipal? performer,
        CancellationToken cancellationToken)
    {
        var maxAutoRetries = options.Value.ScriptMaxAutoRetries <= 0 ? 3 : options.Value.ScriptMaxAutoRetries;

        while (true)
        {
            var result = await executor.ExecuteAsync(request, cancellationToken);
            var offboardingLog = ReadLatestOffboardingLogText(request.Id);
            var outcome = EvaluateScriptOutcome(result.ExitCode, offboardingLog, result.StandardError);

            if (outcome.IsSuccess)
            {
                request.ScriptAutoRetryCount = 0;
                await ApplyScriptResultAsync(request, result.ExitCode, result.StandardError, performer, cancellationToken);
                return;
            }

            if (LeaverScriptFailureClassifier.IsRecoverable(result.ExitCode, offboardingLog, result.StandardError)
                && request.ScriptAutoRetryCount < maxAutoRetries)
            {
                request.ScriptAutoRetryCount++;
                await db.SaveChangesAsync(cancellationToken);

                var retryNotes =
                    $"Automatic retry {request.ScriptAutoRetryCount}/{maxAutoRetries} after recoverable error: {outcome.Notes ?? result.StandardError ?? $"exit code {result.ExitCode}"}";
                logger.LogWarning(
                    "Recoverable script failure for request {RequestId}. {Notes}",
                    request.Id,
                    retryNotes);

                await audit.AddAsync("ScriptAutoRetry", performer, request.Id, notes: retryNotes, cancellationToken: cancellationToken);
                await repairService.TryRepairEnvironmentAsync(cancellationToken);

                request.ScriptLastAttemptAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            await ApplyScriptResultAsync(request, result.ExitCode, result.StandardError, performer, cancellationToken);
            return;
        }
    }

    private async Task ApplyScriptResultAsync(
        LeaverRequest request,
        int exitCode,
        string? executorNotes,
        ClaimsPrincipal? performer,
        CancellationToken cancellationToken)
    {
        var logFolder = options.Value.LogFolderPath;
        Directory.CreateDirectory(logFolder);
        var path = Path.Combine(logFolder, $"{request.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log");

        if (processingMode.IsScriptTestModeEffective)
        {
            await File.WriteAllTextAsync(
                path,
                "ScriptTestMode is ON — LeaverProcess.ps1 was NOT executed. The request remains Scheduled so it can be processed after live mode is enabled.\n",
                cancellationToken);

            request.ScriptExecutionLogPath = path;
            request.Status = LeaverStatus.Scheduled;
            request.ScriptExecutionStatus = ScriptExecutionStatus.NotRun;
            request.ScriptExecutedAt = null;
            request.ScriptLastFailedAt = null;
            await db.SaveChangesAsync(cancellationToken);

            await audit.AddAsync(
                "ScriptTestModeSkipped",
                performer,
                request.Id,
                notes: "Script test mode prevented execution. Switch to live mode on the Ops page before processing real leavers.",
                cancellationToken: cancellationToken);
            return;
        }

        var offboardingLog = ReadLatestOffboardingLogText(request.Id);
        var outcome = EvaluateScriptOutcome(exitCode, offboardingLog, executorNotes);

        await File.WriteAllTextAsync(
            path,
            $"Exit code: {exitCode}\nEvaluated status: {outcome.Status}\nNotes: {outcome.Notes}\n\nOffboarding log excerpt:\n{TrimNotes(offboardingLog, null)}\n",
            cancellationToken);

        request.ScriptExecutionLogPath = path;
        request.ScriptExecutionStatus = outcome.IsSuccess ? ScriptExecutionStatus.Completed : ScriptExecutionStatus.Failed;
        request.Status = outcome.Status;
        request.ScriptExecutedAt = outcome.IsSuccess ? DateTime.UtcNow : null;
        request.ScriptLastFailedAt = outcome.IsSuccess ? null : DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await audit.AddAsync(
            outcome.IsSuccess ? "ScriptCompleted" : "ScriptFailed",
            performer,
            request.Id,
            notes: outcome.Notes,
            cancellationToken: cancellationToken);

        if (!outcome.IsSuccess)
            await NotifyFailureAsync(
                "PowerShell script failed",
                new InvalidOperationException(outcome.Notes ?? $"Exit code {exitCode}"),
                request.Id,
                outcome.Notes,
                cancellationToken);
    }

    private async Task MarkFailedAsync(
        int requestId,
        string? notes,
        ClaimsPrincipal? performer,
        bool notify,
        CancellationToken cancellationToken)
    {
        var request = await db.LeaverRequests.SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);
        if (request is null || request.Status is LeaverStatus.Completed or LeaverStatus.Cancelled)
            return;

        var alreadyFailed = request.Status == LeaverStatus.Failed;
        request.Status = LeaverStatus.Failed;
        request.ScriptExecutionStatus = ScriptExecutionStatus.Failed;
        request.ScriptExecutedAt = null;
        request.ScriptLastFailedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        if (!alreadyFailed)
            await audit.AddAsync("ScriptFailed", performer, requestId, notes: notes, cancellationToken: cancellationToken);

        if (notify && !alreadyFailed)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await NotifyFailureAsync("Leaver disable failed", new InvalidOperationException(notes ?? "Processing failed."), requestId, notes, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Background failure notification could not be sent for request {RequestId}", requestId);
                }
            }, CancellationToken.None);
        }
    }

    private async Task NotifyFailureAsync(string summary, Exception exception, int requestId, string? output = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = await db.LeaverRequests
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);

            var offboardingLog = ReadLatestOffboardingLogText(requestId) ?? output;
            var context = new LeaverFailureNotificationContext(
                request?.LeaverDisplayName,
                request?.LeaverUserPrincipalName,
                offboardingLog);

            await errors.NotifyAsync(summary, exception, requestId, output, context, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not send failure notification email for request {RequestId}", requestId);
        }
    }

    public async Task ReconcileStuckRequestsAsync(CancellationToken cancellationToken = default)
    {
        await ReconcileFinishedPendingRequestsAsync(cancellationToken);
        await ReconcileStalePendingRequestsAsync(cancellationToken);

        var cutoff = DateTime.UtcNow.AddMinutes(-2);
        var stuckScheduledIds = await db.LeaverRequests
            .AsNoTracking()
            .Where(x => x.Status == LeaverStatus.Scheduled
                && x.ScriptExecutedAt == null
                && x.ScriptEligibleAt <= DateTime.UtcNow
                && db.AuditLogs.Any(a => a.LeaverRequestId == x.Id && a.Action == "ScriptStarted" && a.PerformedAt <= cutoff))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in stuckScheduledIds)
        {
            var notes = ReadLatestOffboardingLogExcerpt(id)
                ?? "Processing started but the request status was never updated (likely an app restart during script execution).";
            logger.LogWarning("Reconciling stuck scheduled request {RequestId} to Failed", id);
            await MarkFailedAsync(id, notes, performer: null, notify: true, cancellationToken);
        }

        var longPendingCutoff = DateTime.UtcNow.AddMinutes(-45);
        var stuckPendingIds = await db.LeaverRequests
            .AsNoTracking()
            .Where(x => x.Status == LeaverStatus.Pending
                && x.ScriptExecutedAt == null
                && x.ScriptLastAttemptAt <= longPendingCutoff)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in stuckPendingIds)
        {
            var logText = ReadLatestOffboardingLogText(id);
            if (LeaverScriptFailureClassifier.ScriptFinished(logText))
            {
                await ReconcileRequestAsync(id, cancellationToken);
                continue;
            }

            logger.LogWarning("Reconciling long-running pending request {RequestId} to Failed", id);
            await MarkFailedAsync(
                id,
                "Processing remained pending for over 45 minutes without completing. Check the latest offboarding log and retry if needed.",
                performer: null,
                notify: true,
                cancellationToken);
        }
    }

    private async Task ReconcileFinishedPendingRequestsAsync(CancellationToken cancellationToken)
    {
        var pendingIds = await db.LeaverRequests
            .AsNoTracking()
            .Where(x => x.Status == LeaverStatus.Pending && x.ScriptExecutedAt == null)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in pendingIds)
        {
            var logText = ReadLatestOffboardingLogText(id);
            if (!LeaverScriptFailureClassifier.ScriptFinished(logText))
                continue;

            if (executionTracker.IsRunning(id))
            {
                executionTracker.TryTerminate(id);
                executionTracker.End(id);
            }

            var request = await db.LeaverRequests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (request is null || request.Status != LeaverStatus.Pending || request.ScriptExecutedAt != null)
                continue;

            var exitCode = logText!.Contains("Offboarding failed because", StringComparison.OrdinalIgnoreCase)
                || logText.Contains("Offboarding completed with", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
            logger.LogInformation("Reconciling finished pending request {RequestId} from offboarding log", id);
            await ApplyScriptResultAsync(request, exitCode, executorNotes: null, performer: null, cancellationToken);
        }
    }

    private async Task ReconcileStalePendingRequestsAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var stuckMinutes = settings.ScriptStuckPendingMinutes <= 0 ? 5 : settings.ScriptStuckPendingMinutes;
        var stuckThreshold = TimeSpan.FromMinutes(stuckMinutes);
        var startupGrace = TimeSpan.FromSeconds(90);

        var pendingIds = await db.LeaverRequests
            .AsNoTracking()
            .Where(x => x.Status == LeaverStatus.Pending && x.ScriptExecutedAt == null)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in pendingIds)
        {
            var logText = ReadLatestOffboardingLogText(id);
            if (LeaverScriptFailureClassifier.ScriptFinished(logText))
                continue;

            if (executionTracker.IsRunning(id))
            {
                var pendingRequest = await db.LeaverRequests.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
                var logFile = GetLatestOffboardingLogFile(id);
                var inactiveFor = DateTime.UtcNow - GetLastActivityUtc(pendingRequest?.ScriptLastAttemptAt, logFile);
                if (inactiveFor >= stuckThreshold)
                {
                    logger.LogWarning(
                        "Leaver request {RequestId} script process appears hung (no log activity for {InactiveMinutes:F1}m) — terminating",
                        id,
                        inactiveFor.TotalMinutes);
                    executionTracker.TryTerminate(id);
                    executionTracker.End(id);
                }
                else
                {
                    continue;
                }
            }

            var pending = await db.LeaverRequests.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (pending is null)
                continue;

            var latestLogFile = GetLatestOffboardingLogFile(id);
            var lastActivityUtc = GetLastActivityUtc(pending.ScriptLastAttemptAt, latestLogFile);
            var inactive = DateTime.UtcNow - lastActivityUtc;

            if (latestLogFile is null && inactive < startupGrace)
                continue;

            if (inactive < stuckThreshold)
                continue;

            if (CoreDisableCompletedInLog(logText))
            {
                var request = await db.LeaverRequests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
                if (request is null || request.Status != LeaverStatus.Pending || request.ScriptExecutedAt != null)
                    continue;

                logger.LogInformation(
                    "Reconciling pending request {RequestId}: core disable completed but script did not finish cleanup (inactive {InactiveMinutes:F1}m)",
                    id,
                    inactive.TotalMinutes);
                var exitCode = logText!.Contains("Offboarding completed with", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                await ApplyScriptResultAsync(request, exitCode, executorNotes: null, performer: null, cancellationToken);
                continue;
            }

            if (await TryScheduleAutoRetryAsync(id, inactive, logText, performer: null, cancellationToken))
                continue;

            var excerpt = ReadLatestOffboardingLogExcerptFromText(logText);
            var detail = latestLogFile is null
                ? "No offboarding log was written — the PowerShell process may have failed to start, crashed, or was killed."
                : excerpt ?? $"Offboarding log stopped updating at {latestLogFile.LastWriteTimeUtc:u}.";

            logger.LogWarning(
                "Reconciling stale pending request {RequestId} to Failed after {InactiveMinutes:F1} minutes without activity",
                id,
                inactive.TotalMinutes);
            await MarkFailedAsync(
                id,
                $"Processing appears stuck ({inactive.TotalMinutes:F0} minutes without progress). {detail}",
                performer: null,
                notify: true,
                cancellationToken);
        }
    }

    private async Task<bool> TryScheduleAutoRetryAsync(
        int requestId,
        TimeSpan inactiveFor,
        string? logText,
        ClaimsPrincipal? performer,
        CancellationToken cancellationToken)
    {
        var maxAutoRetries = options.Value.ScriptMaxAutoRetries <= 0 ? 3 : options.Value.ScriptMaxAutoRetries;
        var request = await db.LeaverRequests.SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);
        if (request is null || request.Status != LeaverStatus.Pending || request.ScriptExecutedAt != null)
            return false;

        if (request.ScriptAutoRetryCount >= maxAutoRetries)
            return false;

        if (!LeaverScriptFailureClassifier.IsRecoverable(exitCode: -1, logText, executorNotes: null))
            return false;

        request.ScriptAutoRetryCount++;
        request.Status = LeaverStatus.Scheduled;
        request.ScriptEligibleAt = DateTime.UtcNow;
        request.ScriptLastAttemptAt = null;
        request.ScriptLastFailedAt = null;
        await db.SaveChangesAsync(cancellationToken);

        var notes =
            $"Automatic retry {request.ScriptAutoRetryCount}/{maxAutoRetries} scheduled after {inactiveFor.TotalMinutes:F0} minutes without progress.";
        logger.LogWarning("Scheduling automatic retry for stuck request {RequestId}: {Notes}", requestId, notes);
        await audit.AddAsync("ScriptAutoRetryScheduled", performer, requestId, notes: notes, cancellationToken: cancellationToken);
        await repairService.TryRepairEnvironmentAsync(cancellationToken);
        asapDispatcher.Enqueue(requestId);
        return true;
    }

    private static DateTime GetLastActivityUtc(DateTime? scriptLastAttemptAt, FileInfo? logFile)
    {
        var attemptUtc = NormalizeToUtc(scriptLastAttemptAt);
        var logUtc = logFile?.LastWriteTimeUtc ?? DateTime.MinValue;
        return attemptUtc > logUtc ? attemptUtc : logUtc;
    }

    private static DateTime NormalizeToUtc(DateTime? value)
    {
        if (value is null)
            return DateTime.MinValue;

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }

    private static bool CoreDisableCompletedInLog(string? logText) =>
        LeaverScriptFailureClassifier.CoreDisableCompleted(logText);

    private ScriptOutcome EvaluateScriptOutcome(int exitCode, string? offboardingLog, string? executorNotes)
    {
        if (string.IsNullOrWhiteSpace(offboardingLog))
        {
            return new ScriptOutcome(
                false,
                LeaverStatus.Failed,
                "No offboarding log was written. The disable script probably did not run (check ScriptTestMode and restart the app).");
        }

        var entraDisabled = offboardingLog.Contains("Disabled Entra ID account", StringComparison.OrdinalIgnoreCase)
            || offboardingLog.Contains("Entra ID account already disabled", StringComparison.OrdinalIgnoreCase);
        var adDisabled = offboardingLog.Contains("Disabled AD account", StringComparison.OrdinalIgnoreCase)
            || offboardingLog.Contains("AD account already disabled", StringComparison.OrdinalIgnoreCase)
            || (options.Value.ScriptSandboxSkipAd
                && offboardingLog.Contains("SANDBOX: Skipped AD password reset and disable", StringComparison.OrdinalIgnoreCase));
        var warningMatch = FailureCountPattern.Match(offboardingLog);
        var warningCount = warningMatch.Success ? int.Parse(warningMatch.Groups[1].Value) : 0;

        if (exitCode == PowerShellScriptExecutor.TimeoutExitCode)
        {
            if (entraDisabled && adDisabled)
            {
                return new ScriptOutcome(
                    true,
                    LeaverStatus.Completed,
                    warningCount > 0
                        ? $"Script timed out during cleanup but core disable completed with {warningCount} warning(s). {executorNotes}"
                        : $"Script timed out during non-critical cleanup but Entra and AD accounts were disabled. {executorNotes}");
            }

            return new ScriptOutcome(
                false,
                LeaverStatus.Failed,
                executorNotes
                    ?? ReadLatestOffboardingLogExcerptFromText(offboardingLog)
                    ?? $"Script timed out (exit code {exitCode}) before core disable completed.");
        }

        if (entraDisabled && !adDisabled)
        {
            return new ScriptOutcome(
                false,
                LeaverStatus.Failed,
                "Entra ID was disabled but the on-prem AD account was not. Retry will attempt the remaining AD steps only. "
                + (ReadLatestOffboardingLogExcerptFromText(offboardingLog) ?? $"Script exited with code {exitCode}."));
        }

        if (entraDisabled && adDisabled)
        {
            if (exitCode != 0 || warningCount > 0)
            {
                return new ScriptOutcome(
                    true,
                    LeaverStatus.Completed,
                    warningCount > 0
                        ? $"Core disable completed with {warningCount} non-critical warning(s). See the offboarding log for Exchange/group cleanup details."
                        : "Core disable completed with non-critical warnings. See the offboarding log for details.");
            }

            return new ScriptOutcome(true, LeaverStatus.Completed, null);
        }

        if (exitCode == 0)
            return new ScriptOutcome(true, LeaverStatus.Completed, null);

        return new ScriptOutcome(
            false,
            LeaverStatus.Failed,
            ReadLatestOffboardingLogExcerptFromText(offboardingLog) ?? $"Script exited with code {exitCode}.");
    }

    private FileInfo? GetLatestOffboardingLogFile(int requestId)
    {
        var logFolder = options.Value.LogFolderPath;
        if (string.IsNullOrWhiteSpace(logFolder) || !Directory.Exists(logFolder))
            return null;

        return Directory.EnumerateFiles(logFolder, $"offboarding-{requestId}-*.log")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    private string? ReadLatestOffboardingLogText(int requestId)
    {
        var latest = GetLatestOffboardingLogFile(requestId);
        if (latest is null || !latest.Exists)
            return null;

        try
        {
            return File.ReadAllText(latest.FullName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read offboarding log for request {RequestId}", requestId);
            return null;
        }
    }

    private string? ReadLatestOffboardingLogExcerpt(int requestId, int maxLines = 12) =>
        ReadLatestOffboardingLogExcerptFromText(ReadLatestOffboardingLogText(requestId), maxLines);

    private static string? ReadLatestOffboardingLogExcerptFromText(string? logText, int maxLines = 12)
    {
        if (string.IsNullOrWhiteSpace(logText))
            return null;

        var lines = logText
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Failed:", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Finished offboarding", StringComparison.OrdinalIgnoreCase))
            .TakeLast(maxLines)
            .ToArray();

        if (lines.Length == 0)
            lines = logText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).TakeLast(maxLines).ToArray();

        var excerpt = string.Join(Environment.NewLine, lines);
        return string.IsNullOrWhiteSpace(excerpt) ? null : excerpt;
    }

    private static string? TrimNotes(string? primary, string? secondary)
    {
        var combined = string.Join(Environment.NewLine, new[] { primary, secondary }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(combined))
            return null;
        return combined.Length <= 2000 ? combined : combined[..2000];
    }

    private sealed record ScriptOutcome(bool IsSuccess, LeaverStatus Status, string? Notes);
}
