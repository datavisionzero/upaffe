using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;

namespace Upaffe.Infrastructure.Persistence;

public sealed class HttpMonitorHistoryStore(UpaffeDbContext context) : IHttpMonitorHistoryStore
{
    public async Task<HttpCheckHistoryItem?> ReadCheckAsync(
        string projectKey, string monitorKey, Guid checkId, CancellationToken cancellationToken)
    {
        var monitorId = await LiveMonitorIdAsync(projectKey, monitorKey, cancellationToken);
        if (monitorId is null) return null;
        return await context.HttpChecks.AsNoTracking()
            .Where(value => value.MonitorId == monitorId && value.Id == checkId
                && value.CompletedAt != null)
            .Select(value => new HttpCheckHistoryItem(
                value.Id, value.Sequence, value.Trigger, value.ScheduledFor,
                value.StartedAt, value.CompletedAt!.Value, value.Outcome!.Value,
                value.FailureReason, value.StatusCode, value.ResponseTimeMilliseconds,
                value.EffectiveUrl, value.AppliedToCurrentState))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<HttpCheckHistoryPage?> ListChecksAsync(
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

        var query = context.HttpChecks.AsNoTracking()
            .Where(value => value.MonitorId == monitorId && value.CompletedAt != null);
        if (beforeSequence is not null)
        {
            query = query.Where(value => value.Sequence < beforeSequence);
        }

        var found = await query
            .OrderByDescending(value => value.Sequence)
            .Take(limit + 1)
            .Select(value => new HttpCheckHistoryItem(
                value.Id,
                value.Sequence,
                value.Trigger,
                value.ScheduledFor,
                value.StartedAt,
                value.CompletedAt!.Value,
                value.Outcome!.Value,
                value.FailureReason,
                value.StatusCode,
                value.ResponseTimeMilliseconds,
                value.EffectiveUrl, value.AppliedToCurrentState))
            .ToListAsync(cancellationToken);
        var hasMore = found.Count > limit;
        var items = found.Take(limit).ToArray();
        return new(items, hasMore ? items[^1].Sequence : null);
    }

    public async Task<IncidentHistoryPage?> ListIncidentsAsync(
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

        var query = context.Incidents.AsNoTracking().Where(value => value.MonitorId == monitorId);
        if (beforeOpeningSequence is not null)
        {
            query = query.Where(value => value.OpeningSequence < beforeOpeningSequence);
        }

        var found = await query
            .OrderByDescending(value => value.OpeningSequence)
            .Take(limit + 1)
            .Select(value => new IncidentHistoryItem(
                value.Id,
                value.FirstFailureCheckId,
                value.OpeningCheckId,
                value.LatestFailureCheckId,
                value.ResolutionCheckId,
                value.FirstFailureSequence,
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

    public async Task<IncidentHistoryItem?> ReadIncidentAsync(
        string projectKey, string monitorKey, Guid incidentId, CancellationToken cancellationToken)
    {
        var monitorId = await LiveMonitorIdAsync(projectKey, monitorKey, cancellationToken);
        if (monitorId is null) return null;
        return await context.Incidents.AsNoTracking()
            .Where(value => value.MonitorId == monitorId && value.Id == incidentId)
            .Select(value => new IncidentHistoryItem(
                value.Id, value.FirstFailureCheckId, value.OpeningCheckId,
                value.LatestFailureCheckId, value.ResolutionCheckId,
                value.FirstFailureSequence, value.OpeningSequence,
                value.LatestFailureSequence, value.ResolutionSequence,
                value.BeganAt, value.OpenedAt, value.LastObservedAt,
                value.ResolvedAt, value.OriginalReason, value.LatestReason))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<HttpHistoryPruneResult> PruneAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var incidentsDeleted = await context.Incidents
            .Where(value => value.ResolvedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        var checksDeleted = await context.HttpChecks
            .Where(value =>
                (value.CompletedAt < cutoff
                    || (value.CompletedAt == null
                        && (value.ExecutionLeaseUntil ?? value.StartedAt) < cutoff))
                && !context.HttpMonitors.Any(monitor =>
                    monitor.LatestResultId == value.Id
                    || monitor.LatestSuccessId == value.Id
                    || monitor.FailureStreakStartId == value.Id)
                && !context.Incidents.Any(incident =>
                    incident.FirstFailureCheckId == value.Id
                    || incident.OpeningCheckId == value.Id
                    || incident.LatestFailureCheckId == value.Id
                    || incident.ResolutionCheckId == value.Id))
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(incidentsDeleted, checksDeleted);
    }

    private Task<Guid?> LiveMonitorIdAsync(
        string projectKey,
        string monitorKey,
        CancellationToken cancellationToken) =>
        context.HttpMonitors.AsNoTracking()
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
