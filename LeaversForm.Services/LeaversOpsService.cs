using Microsoft.Extensions.Options;

namespace LeaversForm.Services;

public sealed record LeaversOpsConfig(
    bool ScriptTestMode,
    bool ScriptSandboxSkipAd,
    bool EmailTestMode,
    bool EmailRedirectActive,
    string? EmailRedirectTarget,
    string ItCcAddress,
    string HrCcAddress,
    string ErrorAlertMailbox,
    bool ScriptPathExists,
    string ScriptPath,
    string LogFolderPath,
    bool CertificateConfigured);

public sealed class LeaversOpsService(
    ILeaversFormProcessingModeService processingMode,
    ILeaversFormEmailSettingsService emailSettings,
    IOptions<LeaversFormOptions> leaversOptions,
    GraphOptions graphOptions) : ILeaversOpsService
{
    public Task<LeaversOpsConfig> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        var settings = leaversOptions.Value;
        var email = emailSettings.Current;
        var config = new LeaversOpsConfig(
            processingMode.IsScriptTestModeEffective,
            settings.ScriptSandboxSkipAd,
            graphOptions.EmailTestMode,
            email.RedirectActive,
            email.RedirectActive ? email.RedirectAllEmailsTo : null,
            email.ItCcAddress,
            email.HrCcAddress,
            email.ErrorAlertMailbox,
            !string.IsNullOrWhiteSpace(settings.PowerShellScriptPath) && File.Exists(settings.PowerShellScriptPath),
            settings.PowerShellScriptPath,
            settings.LogFolderPath,
            !string.IsNullOrWhiteSpace(settings.PowerShellCertificateThumbprint));

        return Task.FromResult(config);
    }
}
