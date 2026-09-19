namespace Upaffe.Api.Hosting;

/// <summary>Tracks successful worker iterations without creating monitor observations.</summary>
public sealed class MonitoringProgress(TimeProvider clock)
{
    public static readonly TimeSpan Freshness = TimeSpan.FromMinutes(2);

    private long _lastHttpSuccessTicks;
    private long _lastPushSuccessTicks;

    public void HttpSucceeded() =>
        Interlocked.Exchange(ref _lastHttpSuccessTicks, clock.GetUtcNow().UtcDateTime.Ticks);

    public void PushSucceeded() =>
        Interlocked.Exchange(ref _lastPushSuccessTicks, clock.GetUtcNow().UtcDateTime.Ticks);

    public bool BothFresh()
    {
        var now = clock.GetUtcNow().UtcDateTime.Ticks;
        return Fresh(now, Interlocked.Read(ref _lastHttpSuccessTicks))
            && Fresh(now, Interlocked.Read(ref _lastPushSuccessTicks));
    }

    private static bool Fresh(long now, long last)
    {
        var age = now - last;
        return last != 0 && age >= 0 && age < Freshness.Ticks;
    }
}
