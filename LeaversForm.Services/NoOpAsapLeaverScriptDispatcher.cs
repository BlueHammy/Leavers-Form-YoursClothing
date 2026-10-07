namespace LeaversForm.Services;

/// <summary>
/// Used where background ASAP dispatch is not available (e.g. LeaversForm.Runner).
/// </summary>
public sealed class NoOpAsapLeaverScriptDispatcher : IAsapLeaverScriptDispatcher
{
    public void Enqueue(int requestId)
    {
    }
}
