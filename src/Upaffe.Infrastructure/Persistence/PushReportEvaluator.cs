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
            incident?.Resolve(report);
        }
        else if (incident is null)
        {
            context.PushIncidents.Add(PushIncident.Open(report, report.ReceivedAt));
        }
        else
        {
            incident.ObserveFailure(report);
        }

        return true;
    }
}
