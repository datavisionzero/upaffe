using Upaffe.Application.Monitoring;

namespace Upaffe.Api.Hosting;

/// <summary>Continuously drains durable push deadline work without retaining a database scope while idle.</summary>
public sealed class PushMonitoringService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<PushMonitoringService> logger) : BackgroundService
{
    internal static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Monitoring:Enabled", true))
        {
            logger.LogInformation("Push deadline monitoring is disabled by configuration.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var run = scope.ServiceProvider.GetRequiredService<RunPushDeadline>();
                if (await run.ExecuteOnceAsync(stoppingToken))
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
                logger.LogError(exception, "The push deadline monitoring loop failed and will retry.");
            }

            await Task.Delay(IdleDelay, clock, stoppingToken);
        }
    }
}
