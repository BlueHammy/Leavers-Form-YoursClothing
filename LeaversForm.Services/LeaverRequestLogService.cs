using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeaversForm.Services;

public sealed class LeaverRequestLogService(
    IOptions<LeaversFormOptions> options,
    ILogger<LeaverRequestLogService> logger) : ILeaverRequestLogService
{
    public string? GetLatestOffboardingLogPath(int requestId) =>
        GetLatestOffboardingLogFile(requestId)?.FullName;

    public string? GetLatestExecutionLogPath(int requestId)
    {
        var logFolder = options.Value.LogFolderPath;
        if (string.IsNullOrWhiteSpace(logFolder) || !Directory.Exists(logFolder))
            return null;

        return Directory.EnumerateFiles(logFolder, $"{requestId}_*.log")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    public string? ReadLatestOffboardingLogText(int requestId)
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

    public static bool IsPathWithinFolder(string filePath, string folderPath)
    {
        var fullFile = Path.GetFullPath(filePath);
        var fullFolder = Path.GetFullPath(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar);
        return fullFile.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase);
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
}
