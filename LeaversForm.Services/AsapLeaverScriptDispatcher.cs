using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LeaversForm.Services;

/// <summary>
/// Runs ASAP leaver script processing in the background so form submit returns immediately.
/// </summary>
public interface IAsapLeaverScriptDispatcher
{
    void Enqueue(int requestId);
}

public sealed class AsapLeaverScriptDispatcher(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    ILogger<AsapLeaverScriptDispatcher> logger) : IAsapLeaverScriptDispatcher
{
    public void Enqueue(int requestId)
    {
        if (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            logger.LogWarning(
                "Skipping ASAP processing for request {RequestId} because the application is shutting down",
                requestId);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                logger.LogInformation("Background ASAP processing started for leaver request {RequestId}", requestId);
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ILeaverScriptProcessor>();
                await processor.ProcessRequestAsync(requestId, performer: null, lifetime.ApplicationStopping);
                logger.LogInformation("Background ASAP processing finished for leaver request {RequestId}", requestId);
            }
            catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
            {
                logger.LogInformation(
                    "Background ASAP processing cancelled for request {RequestId} during shutdown",
                    requestId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background ASAP processing failed for leaver request {RequestId}", requestId);
            }
        }, lifetime.ApplicationStopping);
    }
}
