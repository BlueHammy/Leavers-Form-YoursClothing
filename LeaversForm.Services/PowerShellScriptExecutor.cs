using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeaversForm.Data.Entities;

namespace LeaversForm.Services;

public sealed class PowerShellScriptExecutor(
    IOptions<LeaversFormOptions> options,
    IConfiguration configuration,
    ILeaversFormProcessingModeService processingMode,
    ILeaverScriptExecutionTracker executionTracker,
    ILogger<PowerShellScriptExecutor> logger) : IPowerShellScriptExecutor
{
    public const int TimeoutExitCode = 124;

    public async Task<ScriptExecutionResult> ExecuteAsync(LeaverRequest request, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (processingMode.IsScriptTestModeEffective)
        {
            logger.LogInformation("Script test mode: would process {UPN}", request.LeaverUserPrincipalName);
            return new(0, "Test mode", string.Empty);
        }

        if (!File.Exists(settings.PowerShellScriptPath))
            throw new FileNotFoundException("PowerShell script not found.", settings.PowerShellScriptPath);

        ValidateCertificate(settings.PowerShellCertificateThumbprint);

        var timeoutMinutes = settings.ScriptTimeoutMinutes <= 0 ? 15 : settings.ScriptTimeoutMinutes;
        logger.LogInformation(
            "Starting PowerShell for request {RequestId} ({UPN}). Script={ScriptPath} Logs={LogFolder} Timeout={TimeoutMinutes}m",
            request.Id,
            request.LeaverUserPrincipalName,
            settings.PowerShellScriptPath,
            settings.LogFolderPath,
            timeoutMinutes);

        var psi = new ProcessStartInfo(settings.PowerShellExecutable)
        {
            WorkingDirectory = settings.PowerShellWorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(settings.PowerShellScriptPath);
        psi.ArgumentList.Add("-UserPrincipalName");
        psi.ArgumentList.Add(request.LeaverUserPrincipalName);
        if (!string.IsNullOrWhiteSpace(request.SamAccountName))
        {
            psi.ArgumentList.Add("-SamAccountName");
            psi.ArgumentList.Add(request.SamAccountName);
        }

        AddArgument(psi, "-TenantId", settings.PowerShellTenantId);
        AddArgument(psi, "-AppId", settings.PowerShellAppId);
        AddArgument(psi, "-CertificateThumbprint", settings.PowerShellCertificateThumbprint);
        AddArgument(psi, "-ExchangeOrganization", settings.PowerShellExchangeOrganization);
        AddArgument(psi, "-DomainController", settings.PowerShellDomainController);
        AddArgument(psi, "-MaxRuntimeMinutes", timeoutMinutes.ToString());
        if (settings.ScriptSandboxSkipAd)
            psi.ArgumentList.Add("-SandboxSkipAdDisable");
        if (!string.IsNullOrWhiteSpace(settings.LogFolderPath))
        {
            Directory.CreateDirectory(settings.LogFolderPath);
            AddArgument(psi, "-LogPath", Path.Combine(settings.LogFolderPath, $"offboarding-{request.Id}-{DateTime.UtcNow:yyyyMMdd_HHmmss}.log"));
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start PowerShell.");
        executionTracker.AttachProcess(request.Id, process.Id);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                "PowerShell timed out for request {RequestId} after {TimeoutMinutes} minutes — terminating process",
                request.Id,
                timeoutMinutes);

            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not terminate timed-out PowerShell process for request {RequestId}", request.Id);
            }

            var timeoutMessage =
                $"LeaverProcess.ps1 exceeded the {timeoutMinutes} minute timeout and was terminated. " +
                "Check the latest offboarding log — core disable steps may have completed before the timeout.";
            return new(TimeoutExitCode, string.Empty, timeoutMessage);
        }

        logger.LogInformation(
            "PowerShell finished for request {RequestId} with exit code {ExitCode}",
            request.Id,
            process.ExitCode);
        return new(process.ExitCode, string.Empty, string.Empty);
    }

    private void ValidateCertificate(string? thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
            throw new InvalidOperationException("LeaversForm:PowerShellCertificateThumbprint is not configured.");

        var clean = thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal);
        var cert = TryLoadCertificateFromFile(clean) ?? FindCertificate(StoreLocation.CurrentUser, clean)
            ?? FindCertificate(StoreLocation.LocalMachine, clean);

        if (cert is null)
        {
            var hint = OperatingSystem.IsWindows()
                ? "Run .\\Repair-LiveProcessingCertificate.ps1 to import the .pfx backup, or .\\Initialize-LiveProcessingTest.ps1 -RecreateCertificate to create a new cert."
                : "Set LeaversForm:PowerShellCertificatePath to your automation .pfx and LeaversForm__PowerShellCertificatePassword in the environment.";
            throw new InvalidOperationException(
                $"Certificate thumbprint {thumbprint} was not found. {hint}");
        }

        logger.LogInformation(
            "Using certificate {Subject} (expires {Expiry:yyyy-MM-dd})",
            cert.Subject,
            cert.NotAfter);
    }

    private X509Certificate2? TryLoadCertificateFromFile(string expectedThumbprint)
    {
        var path = options.Value.PowerShellCertificatePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var password = configuration["LeaversForm:PowerShellCertificatePassword"];
        var cert = string.IsNullOrEmpty(password)
            ? new X509Certificate2(path)
            : new X509Certificate2(path, password);

        var actual = cert.Thumbprint?.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!string.Equals(actual, expectedThumbprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Certificate at {path} has thumbprint {cert.Thumbprint}, expected {expectedThumbprint}.");
        }

        return cert;
    }

    private static X509Certificate2? FindCertificate(StoreLocation location, string thumbprint)
    {
        using var store = new X509Store(StoreName.My, location);
        store.Open(OpenFlags.ReadOnly);
        return store.Certificates
            .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
            .OfType<X509Certificate2>()
            .FirstOrDefault(c => c.HasPrivateKey);
    }

    private static void AddArgument(ProcessStartInfo psi, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            psi.ArgumentList.Add(name);
            psi.ArgumentList.Add(value);
        }
    }
}
