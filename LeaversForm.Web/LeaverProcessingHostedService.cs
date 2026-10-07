using LeaversForm.Services;

namespace LeaversForm.Web;

/// <summary>
/// Runs due leaver processing inside the web application.
/// IIS must keep the app pool running for this to operate continuously.
/// </summary>
public sealed class LeaverProcessingHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<LeaverProcessingHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ILeaverScriptProcessor>();
                await processor.ProcessDueRequestsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug(ex, "Leaver processing interrupted during shutdown.");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background leaver processing check failed.");
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }
}
