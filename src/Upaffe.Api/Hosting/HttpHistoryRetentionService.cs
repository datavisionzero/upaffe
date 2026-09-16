using Upaffe.Application.Monitoring;

namespace Upaffe.Api.Hosting;

/// <summary>Applies the bounded monitoring-history policy at startup and once per day.</summary>
public sealed class HttpHistoryRetentionService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<HttpHistoryRetentionService> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("HistoryRetention:Enabled", true))
        {
            logger.LogInformation("HTTP history retention is disabled by configuration.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var prune = scope.ServiceProvider.GetRequiredService<PruneHttpMonitorHistory>();
                var result = await prune.ExecuteAsync(stoppingToken);
                logger.LogInformation(
                    "HTTP history retention removed {IncidentCount} incidents and {CheckCount} checks.",
                    result.IncidentsDeleted,
                    result.ChecksDeleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "HTTP history retention failed and will retry tomorrow.");
            }

            await Task.Delay(Interval, clock, stoppingToken);
        }
    }
}
