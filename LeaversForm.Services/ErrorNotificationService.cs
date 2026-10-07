using Microsoft.Extensions.Logging;

namespace LeaversForm.Services;

public sealed class ErrorNotificationService(
    IGraphEmailService email,
    ILeaversFormEmailSettingsService emailSettings,
    ILogger<ErrorNotificationService> logger) : IErrorNotificationService
{
    public async Task NotifyAsync(
        string summary,
        Exception exception,
        int? leaverRequestId = null,
        string? output = null,
        LeaverFailureNotificationContext? leaverContext = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogError(
            exception,
            "LeaversForm failure: {Summary}; Leaver {Id}; Name {Name}; Output {Output}",
            summary,
            leaverRequestId,
            leaverContext?.LeaverDisplayName,
            output);

        try
        {
            var leaverLine = BuildLeaverLine(leaverRequestId, leaverContext);
            var platformTable = BuildPlatformTable(leaverContext);
            var subject = BuildSubject(summary, leaverContext);

            var details = $"""
                <p><strong>Timestamp (UTC):</strong> {DateTime.UtcNow:u}</p>
                {leaverLine}
                {platformTable}
                <h3>Exception</h3>
                <pre style="white-space:pre-wrap;background:#f5f8f6;padding:14px;border-radius:6px;">{EmailTemplate.Encode(exception.ToString())}</pre>
                <h3>Additional output</h3>
                <pre style="white-space:pre-wrap;background:#f5f8f6;padding:14px;border-radius:6px;">{EmailTemplate.Encode(output)}</pre>
                """;

            await email.SendAsync(
                new EmailMessage(
                    emailSettings.ErrorAlertMailbox,
                    subject,
                    EmailTemplate.Build("Leavers Form error", details)),
                cancellationToken);
        }
        catch (Exception notificationException)
        {
            logger.LogError(notificationException, "IT error notification failed");
        }
    }

    private static string BuildSubject(string summary, LeaverFailureNotificationContext? leaverContext)
    {
        if (!string.IsNullOrWhiteSpace(leaverContext?.LeaverDisplayName))
            return $"Leavers Form error: {summary} — {leaverContext.LeaverDisplayName}";

        return $"Leavers Form error: {summary}";
    }

    private static string BuildLeaverLine(int? leaverRequestId, LeaverFailureNotificationContext? leaverContext)
    {
        if (!string.IsNullOrWhiteSpace(leaverContext?.LeaverDisplayName))
        {
            var upn = string.IsNullOrWhiteSpace(leaverContext.LeaverUserPrincipalName)
                ? string.Empty
                : $" ({EmailTemplate.Encode(leaverContext.LeaverUserPrincipalName)})";
            var requestSuffix = leaverRequestId is null ? string.Empty : $" — request #{leaverRequestId}";
            return $"""<p><strong>Leaver:</strong> {EmailTemplate.Encode(leaverContext.LeaverDisplayName)}{upn}{requestSuffix}</p>""";
        }

        if (leaverRequestId is null)
            return """<p><strong>Leaver:</strong> Not request-specific</p>""";

        return $"""<p><strong>Leaver request:</strong> #{leaverRequestId}</p>""";
    }

    private static string BuildPlatformTable(LeaverFailureNotificationContext? leaverContext)
    {
        var log = leaverContext?.OffboardingLog;
        if (string.IsNullOrWhiteSpace(log))
            return string.Empty;

        var status = LeaverScriptPlatformStatus.Evaluate(log);
        if (status is { Graph: PlatformStepStatus.Unknown, Exchange: PlatformStepStatus.Unknown, ActiveDirectory: PlatformStepStatus.Unknown })
            return string.Empty;

        return LeaverScriptPlatformStatus.BuildHtmlTable(status);
    }
}
