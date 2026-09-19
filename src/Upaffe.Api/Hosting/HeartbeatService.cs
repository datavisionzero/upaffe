namespace Upaffe.Api.Hosting;

/// <summary>One bounded heartbeat attempt per interval, independent of monitoring work.</summary>
public sealed class HeartbeatService(HeartbeatSender sender, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!sender.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await sender.SendIfProgressingAsync(stoppingToken);
            await Task.Delay(HeartbeatSender.Interval, clock, stoppingToken);
        }
    }
}
