namespace Upaffe.Api.Hosting;

/// <summary>Sends an empty signal only while both monitoring workers are fresh.</summary>
public sealed class HeartbeatSender(
    HeartbeatDestination destination,
    MonitoringProgress progress,
    HttpClient client,
    ILogger<HeartbeatSender> logger)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public bool Enabled => destination.Url is not null;

    public async Task SendIfProgressingAsync(CancellationToken cancellationToken)
    {
        if (destination.Url is null || !progress.BothFresh())
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, destination.Url);
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("The heartbeat receiver returned HTTP {StatusCode}.",
                    (int)response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("The heartbeat attempt failed ({FailureType}).",
                exception.GetType().Name);
        }
    }
}
