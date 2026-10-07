namespace LeaversForm.Services;

using Microsoft.Extensions.Logging;

public interface ILeaverScriptExecutionTracker
{
    bool TryBegin(int requestId);
    void End(int requestId);
    bool IsRunning(int requestId);
    void AttachProcess(int requestId, int processId);
    bool TryTerminate(int requestId);
}

public sealed class LeaverScriptExecutionTracker(ILogger<LeaverScriptExecutionTracker> logger) : ILeaverScriptExecutionTracker
{
    private readonly HashSet<int> _running = [];
    private readonly Dictionary<int, int> _processIds = [];
    private readonly object _gate = new();

    public bool TryBegin(int requestId)
    {
        lock (_gate)
            return _running.Add(requestId);
    }

    public void End(int requestId)
    {
        lock (_gate)
        {
            _running.Remove(requestId);
            _processIds.Remove(requestId);
        }
    }

    public bool IsRunning(int requestId)
    {
        lock (_gate)
            return _running.Contains(requestId);
    }

    public void AttachProcess(int requestId, int processId)
    {
        lock (_gate)
            _processIds[requestId] = processId;
    }

    public bool TryTerminate(int requestId)
    {
        int processId;
        lock (_gate)
        {
            if (!_processIds.TryGetValue(requestId, out processId))
                return false;
            _processIds.Remove(requestId);
        }

        try
        {
            var process = System.Diagnostics.Process.GetProcessById(processId);
            if (process.HasExited)
                return false;

            process.Kill(entireProcessTree: true);
            logger.LogWarning("Terminated hung PowerShell process {ProcessId} for leaver request {RequestId}", processId, requestId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not terminate PowerShell process {ProcessId} for request {RequestId}", processId, requestId);
            return false;
        }
    }
}
