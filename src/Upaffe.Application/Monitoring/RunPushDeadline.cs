using Upaffe.Application.Ports;

namespace Upaffe.Application.Monitoring;

public sealed class RunPushDeadline(IPushDeadlineStore deadlines, TimeProvider clock)
{
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    public async Task<bool> ExecuteOnceAsync(CancellationToken cancellationToken)
    {
        var lease = await deadlines.ClaimAsync(clock.GetUtcNow(), LeaseDuration, cancellationToken);
        if (lease is null)
        {
            return false;
        }

        await deadlines.CompleteAsync(lease, clock.GetUtcNow(), cancellationToken);
        return true;
    }
}
