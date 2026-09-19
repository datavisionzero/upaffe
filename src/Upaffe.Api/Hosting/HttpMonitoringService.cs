using Upaffe.Application.Monitoring;

namespace Upaffe.Api.Hosting;

/// <summary>Continuously drains durable HTTP work without retaining a database scope during idle time.</summary>
public sealed class HttpMonitoringService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider clock,
    MonitoringProgress progress,
    ILogger<HttpMonitoringService> logger) : BackgroundService
{
    internal static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Monitoring:Enabled", true))
        {
            logger.LogInformation("Scheduled HTTP monitoring is disabled by configuration.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var run = scope.ServiceProvider.GetRequiredService<RunScheduledHttpCheck>();
                var worked = await run.ExecuteOnceAsync(stoppingToken);
                progress.HttpSucceeded();
                if (worked)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "The scheduled HTTP monitoring loop failed ({FailureType}) and will retry.",
                    exception.GetType().Name);
            }

            await Task.Delay(IdleDelay, clock, stoppingToken);
        }
    }
}
