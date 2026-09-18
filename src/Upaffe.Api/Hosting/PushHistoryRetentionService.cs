using Upaffe.Application.Monitoring;

namespace Upaffe.Api.Hosting;

/// <summary>Applies the bounded push-history policy at startup and once per day.</summary>
public sealed class PushHistoryRetentionService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<PushHistoryRetentionService> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("HistoryRetention:Enabled", true))
        {
            logger.LogInformation("Push history retention is disabled by configuration.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var prune = scope.ServiceProvider.GetRequiredService<PrunePushMonitorHistory>();
                var result = await prune.ExecuteAsync(stoppingToken);
                logger.LogInformation(
                    "Push history retention removed {IncidentCount} incidents and {ReportCount} reports.",
                    result.IncidentsDeleted,
                    result.ReportsDeleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Push history retention failed and will retry tomorrow.");
            }

            await Task.Delay(Interval, clock, stoppingToken);
        }
    }
}
