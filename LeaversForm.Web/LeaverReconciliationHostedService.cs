using LeaversForm.Services;

namespace LeaversForm.Web;

/// <summary>
/// Runs stuck-request reconciliation on its own timer so it is never blocked by a long-running script execution.
/// </summary>
public sealed class LeaverReconciliationHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<LeaverReconciliationHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ILeaverScriptProcessor>();
                await processor.ReconcileStuckRequestsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Leaver reconciliation check failed.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }
}
