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

        if (check.Outcome != CheckOutcome.Failure
            || monitor.ConsecutiveFailures < monitor.FailureThreshold
            || await context.Incidents.AnyAsync(
                value => value.MonitorId == monitor.Id && value.ResolvedAt == null,
                cancellationToken))
        {
            return true;
        }

        var firstFailureId = monitor.FailureStreakStartId
            ?? throw new InvalidOperationException("A failure streak needs its first check.");
        var firstFailure = firstFailureId == check.Id
            ? check
            : await context.HttpChecks.SingleAsync(value => value.Id == firstFailureId, cancellationToken);
        context.Incidents.Add(Incident.Open(firstFailure, check, now));
        return true;
    }
}
