namespace LeaversForm.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public interface ILeaverScriptRepairService
{
    Task<bool> TryRepairEnvironmentAsync(CancellationToken cancellationToken = default);
}

public sealed class LeaverScriptRepairService(
    IOptions<LeaversFormOptions> options,
    ILogger<LeaverScriptRepairService> logger) : ILeaverScriptRepairService
{
    public async Task<bool> TryRepairEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var repairScript = ResolveRepairScriptPath(settings);
        if (repairScript is null)
        {
            logger.LogWarning("Repair-LeaversFormModules.ps1 was not found — skipping module repair");
            return false;
        }

        logger.LogInformation("Running automatic module repair: {ScriptPath}", repairScript);

        var psi = new System.Diagnostics.ProcessStartInfo(settings.PowerShellExecutable)
        {
            WorkingDirectory = settings.PowerShellWorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(repairScript);

        using var process = System.Diagnostics.Process.Start(psi);
        if (process is null)
            return false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(8));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not terminate timed-out module repair process");
            }

            logger.LogWarning("Module repair timed out after 8 minutes");
            return false;
        }

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            logger.LogWarning("Module repair exited with code {ExitCode}. stderr: {StdErr}", process.ExitCode, stderr);
            return false;
        }

        logger.LogInformation("Module repair completed successfully");
        return true;
    }

    private static string? ResolveRepairScriptPath(LeaversFormOptions settings)
    {
        var candidates = new[]
        {
            Path.Combine(settings.PowerShellWorkingDirectory, "..", "Repair-LeaversFormModules.ps1"),
            Path.Combine(settings.PowerShellWorkingDirectory, "Repair-LeaversFormModules.ps1"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Repair-LeaversFormModules.ps1"))
        };

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);
    }
}
