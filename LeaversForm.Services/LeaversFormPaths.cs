using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace LeaversForm.Services;

public static class LeaversFormPaths
{
    public static void Configure(LeaversFormOptions options, IHostEnvironment environment)
    {
        if (!OperatingSystem.IsWindows())
        {
            if (string.IsNullOrWhiteSpace(options.PowerShellExecutable)
                || options.PowerShellExecutable.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase))
                options.PowerShellExecutable = "pwsh";
        }

        string scriptsDir;
        string logsDir;

        if (environment.IsProduction())
        {
            scriptsDir = environment.ContentRootPath;
            logsDir = Path.Combine(environment.ContentRootPath, "logs");
        }
        else
        {
            var projectRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, ".."));
            scriptsDir = Path.Combine(projectRoot, "scripts");
            logsDir = Path.Combine(projectRoot, "logs");
        }

        Directory.CreateDirectory(scriptsDir);
        Directory.CreateDirectory(logsDir);

        if (ShouldReplaceLegacyPath(options.PowerShellScriptPath))
            options.PowerShellScriptPath = Path.Combine(scriptsDir, "LeaverProcess.ps1");

        if (ShouldReplaceLegacyPath(options.PowerShellWorkingDirectory))
            options.PowerShellWorkingDirectory = scriptsDir;

        if (ShouldReplaceLegacyPath(options.LogFolderPath))
            options.LogFolderPath = logsDir;

        SyncBundledScript(environment, options.PowerShellScriptPath);

        var scriptsDirectory = Path.GetDirectoryName(options.PowerShellScriptPath);
        if (!string.IsNullOrWhiteSpace(scriptsDirectory))
            SyncBundledFile(environment, scriptsDirectory, "LeaversForm-ModuleBootstrap.ps1");
    }

    public static void ConfigureFromConfiguration(LeaversFormOptions options, IConfiguration configuration, IHostEnvironment environment)
    {
        configuration.GetSection("LeaversForm").Bind(options);
        Configure(options, environment);
    }

    private static bool ShouldReplaceLegacyPath(string? path) =>
        string.IsNullOrWhiteSpace(path)
        || path.StartsWith("C:\\Scripts", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("C:\\Logs", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/opt/leaversform/scripts", StringComparison.Ordinal)
        || path.StartsWith("/var/log/leaversform", StringComparison.Ordinal);

    private static void SyncBundledScript(IHostEnvironment environment, string destinationPath)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "LeaverProcess.ps1"),
            Path.Combine(environment.ContentRootPath, "LeaverProcess.ps1"),
            Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "LeaverProcess.ps1"))
        };

        var source = candidates.FirstOrDefault(File.Exists);
        if (source is null)
            return;

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(destinationPath) && File.GetLastWriteTimeUtc(destinationPath) >= File.GetLastWriteTimeUtc(source))
            return;

        File.Copy(source, destinationPath, overwrite: true);
    }

    private static void SyncBundledFile(IHostEnvironment environment, string destinationDirectory, string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(environment.ContentRootPath, fileName),
            Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "scripts", fileName)),
            Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", fileName))
        };

        var source = candidates.FirstOrDefault(File.Exists);
        if (source is null)
            return;

        Directory.CreateDirectory(destinationDirectory);
        File.Copy(source, Path.Combine(destinationDirectory, fileName), overwrite: true);
    }
}
