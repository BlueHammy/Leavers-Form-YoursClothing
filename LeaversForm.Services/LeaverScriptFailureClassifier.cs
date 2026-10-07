namespace LeaversForm.Services;

public static class LeaverScriptFailureClassifier
{
    private static readonly string[] RecoverablePatterns =
    [
        "Module could not be correctly formed",
        "Assembly with same name is already loaded",
        "Could not load file or assembly",
        "Could not load type",
        "module could not be loaded",
        "429",
        "Too Many Requests",
        "503",
        "Service Unavailable",
        "temporarily unavailable",
        "timed out",
        "timeout",
        "The operation has timed out",
        "A connection attempt failed",
        "An existing connection was forcibly closed",
        "Script runtime limit",
        "exceeded the",
        "minute timeout and was terminated",
        "No offboarding log was written",
        "Processing appears stuck",
        "Exchange connect with certificate object failed"
    ];

    private static readonly string[] UnrecoverablePatterns =
    [
        "Certificate with thumbprint",
        "does not have a private key",
        "Certificate",
        "expired on",
        "was not found in Cert",
        "User not found",
        "Could not resolve user",
        "Graph user not resolved",
        "AD user not resolved",
        "ScriptTestMode is ON",
        "Unauthorized",
        "403",
        "Access is denied",
        "Insufficient access rights",
        "Insufficient privileges",
        "Invalid client secret"
    ];

    public static bool CoreDisableCompleted(string? offboardingLog) =>
        !string.IsNullOrWhiteSpace(offboardingLog)
        && (offboardingLog.Contains("Disabled Entra ID account", StringComparison.OrdinalIgnoreCase)
            || offboardingLog.Contains("Entra ID account already disabled", StringComparison.OrdinalIgnoreCase))
        && (offboardingLog.Contains("Disabled AD account", StringComparison.OrdinalIgnoreCase)
            || offboardingLog.Contains("AD account already disabled", StringComparison.OrdinalIgnoreCase)
            || offboardingLog.Contains("SANDBOX: Skipped AD password reset and disable", StringComparison.OrdinalIgnoreCase));

    public static bool ScriptFinished(string? offboardingLog) =>
        !string.IsNullOrWhiteSpace(offboardingLog)
        && offboardingLog.Contains("Finished offboarding", StringComparison.OrdinalIgnoreCase);

    public static bool IsRecoverable(int exitCode, string? offboardingLog, string? executorNotes)
    {
        if (ScriptFinished(offboardingLog) || CoreDisableCompleted(offboardingLog))
            return false;

        var haystack = string.Join(
            Environment.NewLine,
            new[] { offboardingLog, executorNotes, exitCode.ToString() }.Where(x => !string.IsNullOrWhiteSpace(x)));

        if (string.IsNullOrWhiteSpace(haystack))
            return exitCode == PowerShellScriptExecutor.TimeoutExitCode;

        if (UnrecoverablePatterns.Any(pattern => haystack.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (RecoverablePatterns.Any(pattern => haystack.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            return true;

        return exitCode == PowerShellScriptExecutor.TimeoutExitCode;
    }
}
