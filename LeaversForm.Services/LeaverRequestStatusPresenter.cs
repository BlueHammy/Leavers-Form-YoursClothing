using LeaversForm.Data.Entities;
using LeaversForm.Data.Enums;
using Microsoft.Extensions.Options;

namespace LeaversForm.Services;

public sealed class LeaverRequestStatusPresenter(IOptions<LeaversFormOptions> options) : ILeaverRequestStatusPresenter
{
    private static readonly TimeZoneInfo UkTimeZone = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "GMT Standard Time" : "Europe/London");

    public string? BuildStatusDetail(LeaverRequest request)
    {
        var log = ReadLatestOffboardingLog(request.Id);

        return request.Status switch
        {
            LeaverStatus.AwaitingApproval => "Waiting for approval from IT, an admin, or an authorising manager before the account can be switched off",
            LeaverStatus.Scheduled when request.DisableAsap => "Queued for immediate processing",
            LeaverStatus.Scheduled => $"Runs at {FormatUkDateTime(request.ScriptEligibleAt)}",
            LeaverStatus.Pending => "Switching off their account — usually takes 1–3 minutes",
            LeaverStatus.Completed when HasNonCriticalWarnings(log) =>
                "Account switched off — some mailbox cleanup had warnings",
            LeaverStatus.Completed => "Account switched off",
            LeaverStatus.Failed => BuildFailedDetail(request, log),
            _ => null
        };
    }

    public string? BuildFailureGuidance(LeaverRequest request)
    {
        if (request.Status != LeaverStatus.Failed)
            return null;

        return $"We could not fully switch off this account. IT has been notified. Reference: request #{request.Id} — {request.LeaverDisplayName}.";
    }

    private static string BuildFailedDetail(LeaverRequest request, string? log)
    {
        var timeSuffix = request.ScriptLastFailedAt is DateTime failedAt
            ? $" · {failedAt.ToLocalTime():dd/MM/yyyy HH:mm}"
            : string.Empty;

        return string.IsNullOrEmpty(timeSuffix)
            ? "Something went wrong — IT has been notified"
            : $"Something went wrong — IT has been notified{timeSuffix}";
    }

    private static bool HasNonCriticalWarnings(string? log) =>
        !string.IsNullOrWhiteSpace(log)
        && log.Contains("Offboarding completed with", StringComparison.OrdinalIgnoreCase)
        && log.Contains("failed action", StringComparison.OrdinalIgnoreCase);

    private static string FormatUkDateTime(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, UkTimeZone);
        return $"{local:h:mm tt} UK on {local:dd MMM yyyy}";
    }

    private string? ReadLatestOffboardingLog(int requestId)
    {
        var logFolder = options.Value.LogFolderPath;
        if (string.IsNullOrWhiteSpace(logFolder) || !Directory.Exists(logFolder))
            return null;

        var latest = Directory.EnumerateFiles(logFolder, $"offboarding-{requestId}-*.log")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();

        if (latest is null)
            return null;

        try
        {
            return File.ReadAllText(latest.FullName);
        }
        catch
        {
            return null;
        }
    }
}
