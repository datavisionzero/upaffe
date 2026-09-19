using Upaffe.Application.Notifications;

namespace Upaffe.Api.Hosting;

public sealed class EmailHistoryRetentionService(IServiceScopeFactory scopes,
    IConfiguration configuration, TimeProvider clock,
    ILogger<EmailHistoryRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("HistoryRetention:Enabled", true))
        {
            logger.LogInformation("Email history retention is disabled by configuration.");
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<PruneEmailHistory>()
                    .ExecuteAsync(stoppingToken);
                logger.LogInformation(
                    "Email history retention removed {DeliveryCount} deliveries and {WindowCount} windows.",
                    result.DeliveriesDeleted, result.WindowsDeleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception)
            {
                logger.LogError("Email history retention failed and will retry tomorrow.");
            }
            await Task.Delay(TimeSpan.FromDays(1), clock, stoppingToken);
        }
    }
}
