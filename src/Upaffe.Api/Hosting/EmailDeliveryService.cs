using Upaffe.Application.Notifications;

namespace Upaffe.Api.Hosting;

public sealed class EmailDeliveryService(IServiceScopeFactory scopes,
    IConfiguration configuration, TimeProvider clock,
    ILogger<EmailDeliveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("EmailDelivery:Enabled", true))
        {
            logger.LogInformation("Email delivery is disabled by configuration.");
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var run = scope.ServiceProvider.GetRequiredService<RunEmailDelivery>();
                if (await run.ExecuteOnceAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception)
            {
                // SMTP failures can contain private server text. Keep this log fixed.
                logger.LogError("The email delivery loop failed and will retry.");
            }
            await Task.Delay(TimeSpan.FromSeconds(1), clock, stoppingToken);
        }
    }
}
