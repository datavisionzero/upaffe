using Microsoft.EntityFrameworkCore;
using Upaffe.Domain.Monitoring;

namespace Upaffe.Infrastructure.Persistence;

internal static class HttpCheckEvaluator
{
    public static async Task<bool> ApplyAsync(
        UpaffeDbContext context,
        HttpMonitor monitor,
        HttpCheck check,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!monitor.ApplyResult(check, now))
        {
            return false;
        }

        var incident = await context.Incidents.SingleOrDefaultAsync(
            value => value.MonitorId == monitor.Id && value.ResolvedAt == null,
            cancellationToken);
        if (incident is not null)
        {
            if (check.Outcome == CheckOutcome.Success)
            {
                incident.Resolve(check);
                await NotificationIntentFactory.ResolveHttpAsync(context, monitor,
                    incident, now, cancellationToken);
            }
            else
            {
                incident.ObserveFailure(check);
            }

            return true;
        }

        if (check.Outcome != CheckOutcome.Failure
            || monitor.ConsecutiveFailures < monitor.FailureThreshold)
        {
            return true;
        }

        var firstFailureId = monitor.FailureStreakStartId
            ?? throw new InvalidOperationException("A failure streak needs its first check.");
        var firstFailure = firstFailureId == check.Id
            ? check
            : await context.HttpChecks.SingleAsync(value => value.Id == firstFailureId, cancellationToken);
        var opened = Incident.Open(firstFailure, check, now);
        context.Incidents.Add(opened);
        await NotificationIntentFactory.OpenHttpAsync(context, monitor, opened,
            now, cancellationToken);
        return true;
    }
}
