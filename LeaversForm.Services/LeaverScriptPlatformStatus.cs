namespace LeaversForm.Services;

public enum PlatformStepStatus
{
    Unknown,
    Success,
    Failed,
    Skipped
}

public sealed record LeaverPlatformStatus(
    PlatformStepStatus Graph,
    PlatformStepStatus Exchange,
    PlatformStepStatus ActiveDirectory);

public static class LeaverScriptPlatformStatus
{
    public static LeaverPlatformStatus Evaluate(string? offboardingLog)
    {
        if (string.IsNullOrWhiteSpace(offboardingLog))
            return new LeaverPlatformStatus(PlatformStepStatus.Unknown, PlatformStepStatus.Unknown, PlatformStepStatus.Unknown);

        var graph = EvaluateGraph(offboardingLog);
        var exchange = EvaluateExchange(offboardingLog, graph);
        var ad = EvaluateActiveDirectory(offboardingLog);

        return new LeaverPlatformStatus(graph, exchange, ad);
    }

    public static string BuildPlainTextSummary(string? offboardingLog)
    {
        if (string.IsNullOrWhiteSpace(offboardingLog))
            return string.Empty;

        var status = Evaluate(offboardingLog);
        if (status is { Graph: PlatformStepStatus.Unknown, Exchange: PlatformStepStatus.Unknown, ActiveDirectory: PlatformStepStatus.Unknown })
            return string.Empty;

        static string Label(PlatformStepStatus value) => value switch
        {
            PlatformStepStatus.Success => "OK",
            PlatformStepStatus.Failed => "failed",
            PlatformStepStatus.Skipped => "skipped",
            _ => "unknown"
        };

        return $"Graph: {Label(status.Graph)} · Exchange: {Label(status.Exchange)} · AD: {Label(status.ActiveDirectory)}";
    }

    public static string BuildHtmlTable(LeaverPlatformStatus status) =>
        $"""
            <h3 style="margin:22px 0 10px;color:#0b3d2e;">Platform status</h3>
            <table style="border-collapse:collapse;width:100%;max-width:420px;font-size:14px;">
              <tbody>
                {BuildRow("Graph", status.Graph)}
                {BuildRow("Exchange", status.Exchange)}
                {BuildRow("AD", status.ActiveDirectory)}
              </tbody>
            </table>
            """;

    private static PlatformStepStatus EvaluateGraph(string log)
    {
        if (log.Contains("Connected to Microsoft Graph as app-only", StringComparison.OrdinalIgnoreCase)
            || log.Contains("Completed: Connect to Microsoft Graph using app-only certificate authentication", StringComparison.OrdinalIgnoreCase))
            return PlatformStepStatus.Success;

        if (log.Contains("Failed: Connect to Microsoft Graph", StringComparison.OrdinalIgnoreCase))
            return PlatformStepStatus.Failed;

        return PlatformStepStatus.Unknown;
    }

    private static PlatformStepStatus EvaluateExchange(string log, PlatformStepStatus graphStatus)
    {
        if (log.Contains("Connected to Exchange Online using app-only certificate authentication", StringComparison.OrdinalIgnoreCase)
            || log.Contains("Completed: Connect to Exchange Online using app-only certificate authentication", StringComparison.OrdinalIgnoreCase))
            return PlatformStepStatus.Success;

        if (log.Contains("Failed: Connect to Exchange Online", StringComparison.OrdinalIgnoreCase)
            || log.Contains("Skipping all Exchange Online mailbox actions — Exchange Online is not connected", StringComparison.OrdinalIgnoreCase))
            return PlatformStepStatus.Failed;

        if (graphStatus is PlatformStepStatus.Failed or PlatformStepStatus.Unknown)
            return PlatformStepStatus.Skipped;

        return PlatformStepStatus.Unknown;
    }

    private static PlatformStepStatus EvaluateActiveDirectory(string log)
    {
        if (log.Contains("SANDBOX: Skipped AD password reset and disable", StringComparison.OrdinalIgnoreCase)
            || log.Contains("SANDBOX: Skipping on-prem AD dial-in and group cleanup", StringComparison.OrdinalIgnoreCase))
            return PlatformStepStatus.Skipped;

        if (log.Contains("Disabled AD account:", StringComparison.OrdinalIgnoreCase)
            || log.Contains("AD account already disabled:", StringComparison.OrdinalIgnoreCase))
            return PlatformStepStatus.Success;

        if (log.Contains("Failed: Disable on-prem AD account", StringComparison.OrdinalIgnoreCase)
            || log.Contains("Failed: Reset on-prem AD password", StringComparison.OrdinalIgnoreCase)
            || log.Contains("Failed: Resolve on-prem AD user", StringComparison.OrdinalIgnoreCase)
            || (log.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
                && log.Contains("AD", StringComparison.OrdinalIgnoreCase))
            || log.Contains("Insufficient access rights", StringComparison.OrdinalIgnoreCase))
            return PlatformStepStatus.Failed;

        if (log.Contains("AD user not resolved", StringComparison.OrdinalIgnoreCase))
            return PlatformStepStatus.Skipped;

        return PlatformStepStatus.Unknown;
    }

    private static string BuildRow(string label, PlatformStepStatus status)
    {
        var (text, colour) = status switch
        {
            PlatformStepStatus.Success => ("SUCCESS", "#176044"),
            PlatformStepStatus.Failed => ("FAILED", "#b91c1c"),
            PlatformStepStatus.Skipped => ("SKIPPED", "#60756b"),
            _ => ("UNKNOWN", "#92400e")
        };

        return $"""
                <tr>
                  <td style="padding:8px 12px;border:1px solid #d6e2da;font-weight:600;background:#f5f8f6;">{EmailTemplate.Encode(label)}</td>
                  <td style="padding:8px 12px;border:1px solid #d6e2da;color:{colour};font-weight:700;">{text}</td>
                </tr>
                """;
    }
}
