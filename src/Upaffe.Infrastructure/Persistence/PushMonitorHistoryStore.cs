using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;

namespace Upaffe.Infrastructure.Persistence;

public sealed class PushMonitorHistoryStore(UpaffeDbContext context) : IPushMonitorHistoryStore
{
    public async Task<PushReportHistoryItem?> ReadReportAsync(
        string projectKey, string monitorKey, Guid reportId, CancellationToken cancellationToken)
    {
        var monitorId = await LiveMonitorIdAsync(projectKey, monitorKey, cancellationToken);
        if (monitorId is null) return null;
        return await context.PushReports.AsNoTracking()
            .Where(value => value.MonitorId == monitorId && value.Id == reportId)
            .Select(value => new PushReportHistoryItem(
                value.Id, value.ReportId, value.EvaluationGeneration,
                value.Sequence, value.ObservedAt, value.ReceivedAt, value.Outcome,
                value.Outcome == Domain.Monitoring.ReportOutcome.Success
                    ? null : value.IsDeadlineObservation ? "report_missing" : "reported_failure",
                value.Applicable))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<PushReportHistoryPage?> ListReportsAsync(
        string projectKey,
        string monitorKey,
        long? beforeSequence,
        int limit,
        CancellationToken cancellationToken)
    {
        var monitorId = await LiveMonitorIdAsync(projectKey, monitorKey, cancellationToken);
        if (monitorId is null)
        {
            return null;
        }

        var query = context.PushReports.AsNoTracking().Where(value => value.MonitorId == monitorId);
        if (beforeSequence is not null)
        {
            query = query.Where(value => value.Sequence < beforeSequence);
        }

        var found = await query
            .OrderByDescending(value => value.Sequence)
            .Take(limit + 1)
            .Select(value => new PushReportHistoryItem(
                value.Id,
                value.ReportId,
                value.EvaluationGeneration,
                value.Sequence,
                value.ObservedAt,
                value.ReceivedAt,
                value.Outcome,
                value.Outcome == Domain.Monitoring.ReportOutcome.Success
                    ? null
                    : value.IsDeadlineObservation ? "report_missing" : "reported_failure",
                value.Applicable))
            .ToListAsync(cancellationToken);
        var hasMore = found.Count > limit;
        var items = found.Take(limit).ToArray();
        return new(items, hasMore ? items[^1].Sequence : null);
    }

    public async Task<PushIncidentHistoryPage?> ListIncidentsAsync(
        string projectKey,
        string monitorKey,
        long? beforeOpeningSequence,
        int limit,
        CancellationToken cancellationToken)
    {
        var monitorId = await LiveMonitorIdAsync(projectKey, monitorKey, cancellationToken);
        if (monitorId is null)
        {
            return null;
        }

        var query = context.PushIncidents.AsNoTracking().Where(value => value.MonitorId == monitorId);
        if (beforeOpeningSequence is not null)
        {
            query = query.Where(value => value.OpeningSequence < beforeOpeningSequence);
        }

        var found = await query
            .OrderByDescending(value => value.OpeningSequence)
            .Take(limit + 1)
            .Select(value => new PushIncidentHistoryItem(
                value.Id,
                value.OpeningReportId,
                value.LatestFailureReportId,
                value.ResolutionReportId,
                value.OpeningSequence,
                value.LatestFailureSequence,
                value.ResolutionSequence,
                value.BeganAt,
                value.OpenedAt,
                value.LastObservedAt,
                value.ResolvedAt,
                value.OriginalReason,
                value.LatestReason))
            .ToListAsync(cancellationToken);
        var hasMore = found.Count > limit;
        var items = found.Take(limit).ToArray();
        return new(items, hasMore ? items[^1].OpeningSequence : null);
    }

    public async Task<PushIncidentHistoryItem?> ReadIncidentAsync(
        string projectKey, string monitorKey, Guid incidentId, CancellationToken cancellationToken)
    {
        var monitorId = await LiveMonitorIdAsync(projectKey, monitorKey, cancellationToken);
        if (monitorId is null) return null;
        return await context.PushIncidents.AsNoTracking()
            .Where(value => value.MonitorId == monitorId && value.Id == incidentId)
            .Select(value => new PushIncidentHistoryItem(
                value.Id, value.OpeningReportId, value.LatestFailureReportId,
                value.ResolutionReportId, value.OpeningSequence,
                value.LatestFailureSequence, value.ResolutionSequence,
                value.BeganAt, value.OpenedAt, value.LastObservedAt,
                value.ResolvedAt, value.OriginalReason, value.LatestReason))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<PushHistoryPruneResult> PruneAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var incidentsDeleted = await context.PushIncidents
            .Where(value => value.ResolvedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var reportsDeleted = await context.PushReports
            .Where(value => value.ReceivedAt < cutoff
                && !context.PushMonitors.Any(monitor =>
                    monitor.LatestReportId == value.Id || monitor.LatestSuccessId == value.Id)
                && !context.PushIncidents.Any(incident =>
                    incident.OpeningReportId == value.Id
                    || incident.LatestFailureReportId == value.Id
                    || incident.ResolutionReportId == value.Id))
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(incidentsDeleted, reportsDeleted);
    }

    private Task<Guid?> LiveMonitorIdAsync(
        string projectKey,
        string monitorKey,
        CancellationToken cancellationToken) =>
        context.PushMonitors.AsNoTracking()
            .Join(
                context.Projects.AsNoTracking(),
                monitor => monitor.ProjectId,
                project => project.Id,
                (monitor, project) => new { Monitor = monitor, Project = project })
            .Where(value => value.Project.Key == projectKey
                && value.Project.DeletedAt == null
                && value.Monitor.Key == monitorKey
                && value.Monitor.DeletedAt == null)
            .Select(value => (Guid?)value.Monitor.Id)
            .SingleOrDefaultAsync(cancellationToken);
}
