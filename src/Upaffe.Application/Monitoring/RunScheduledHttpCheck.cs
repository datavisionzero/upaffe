using Upaffe.Application.Ports;

namespace Upaffe.Application.Monitoring;

public sealed class RunScheduledHttpCheck(
    IScheduledHttpCheckStore checks,
    IHttpCheckExecutor executor,
    TimeProvider clock)
{
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    public async Task<bool> ExecuteOnceAsync(CancellationToken cancellationToken)
    {
        var lease = await checks.ClaimAsync(clock.GetUtcNow(), LeaseDuration, cancellationToken);
        if (lease is null)
        {
            return false;
        }

        var result = await executor.ExecuteAsync(lease.Request, cancellationToken);

        await checks.CompleteAsync(
            lease.CheckId,
            lease.Token,
            result,
            clock.GetUtcNow(),
            cancellationToken);
        return true;
    }
}
