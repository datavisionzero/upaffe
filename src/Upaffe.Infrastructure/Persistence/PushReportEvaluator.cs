using Microsoft.EntityFrameworkCore;
using Upaffe.Domain.Monitoring;

namespace Upaffe.Infrastructure.Persistence;

internal static class PushReportEvaluator
{
    public static async Task<bool> ApplyAsync(
        UpaffeDbContext context,
        PushMonitor monitor,
        PushReport report,
        CancellationToken cancellationToken)
    {
        if (!monitor.ApplyReport(report))
        {
            return false;
        }

        var incident = await context.PushIncidents.SingleOrDefaultAsync(
            value => value.MonitorId == monitor.Id && value.ResolvedAt == null,
            cancellationToken);
        if (report.Outcome == ReportOutcome.Success)
        {
            if (incident is not null)
            {
                incident.Resolve(report);
                await NotificationIntentFactory.ResolvePushAsync(context, monitor,
                    incident, report.ReceivedAt, cancellationToken);
            }
        }
        else if (incident is null)
        {
            var opened = PushIncident.Open(report, report.ReceivedAt);
            context.PushIncidents.Add(opened);
            await NotificationIntentFactory.OpenPushAsync(context, monitor, opened,
                report.ReceivedAt, cancellationToken);
        }
        else
        {
            incident.ObserveFailure(report);
        }

        return true;
    }
}
