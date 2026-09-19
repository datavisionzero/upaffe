using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Notifications;

namespace Upaffe.Infrastructure.Persistence;

/// <summary>One instance snapshot assembled with a fixed number of read queries.</summary>
public sealed class InstanceOverviewStore(UpaffeDbContext context) : IInstanceOverviewStore
{
    public async Task<InstanceOverview> ReadAsync(DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var projects = await context.Projects.AsNoTracking()
            .Where(value => value.DeletedAt == null)
            .ToListAsync(cancellationToken);
        var projectIds = projects.Select(value => value.Id).ToArray();
        var projectKeys = projects.Select(value => value.Key).ToArray();
        var http = await context.HttpMonitors.AsNoTracking()
            .Where(value => projectIds.Contains(value.ProjectId) && value.DeletedAt == null)
            .ToListAsync(cancellationToken);
        var push = await context.PushMonitors.AsNoTracking()
            .Where(value => projectIds.Contains(value.ProjectId) && value.DeletedAt == null)
            .ToListAsync(cancellationToken);
        var checkIds = http.SelectMany(value => new[] { value.LatestResultId, value.LatestSuccessId })
            .Where(value => value.HasValue).Select(value => value!.Value).Distinct().ToArray();
        var reportIds = push.SelectMany(value => new[] { value.LatestReportId, value.LatestSuccessId })
            .Where(value => value.HasValue).Select(value => value!.Value).Distinct().ToArray();
        var checks = await context.HttpChecks.AsNoTracking()
            .Where(value => checkIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var reports = await context.PushReports.AsNoTracking()
            .Where(value => reportIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var httpIds = http.Select(value => value.Id).ToArray();
        var pushIds = push.Select(value => value.Id).ToArray();
        var httpIncidents = await context.Incidents.AsNoTracking()
            .Where(value => httpIds.Contains(value.MonitorId) && value.ResolvedAt == null)
            .ToDictionaryAsync(value => value.MonitorId, cancellationToken);
        var pushIncidents = await context.PushIncidents.AsNoTracking()
            .Where(value => pushIds.Contains(value.MonitorId) && value.ResolvedAt == null)
            .ToDictionaryAsync(value => value.MonitorId, cancellationToken);
        var scopeIds = projectIds.Concat(httpIds).Concat(pushIds).ToArray();
        var windows = await context.MaintenanceWindows.AsNoTracking()
            .Where(value => scopeIds.Contains(value.ScopeId)
                && value.EndedAt == null && value.EndsAt > now)
            .ToListAsync(cancellationToken);
        var active = windows.GroupBy(value => (value.ScopeType, value.ScopeId))
            .ToDictionary(group => group.Key, group => group.Max(value => value.EndsAt));
        var deliveries = await context.NotificationDeliveries.AsNoTracking()
            .Where(value => projectKeys.Contains(value.ProjectKey))
            .GroupBy(value => new { value.ProjectKey, value.State })
            .Select(group => new { group.Key.ProjectKey, group.Key.State,
                Count = group.Count(), Oldest = group.Min(value => value.CreatedAt),
                Overdue = group.Count(value =>
                    (value.State == DeliveryState.Queued || value.State == DeliveryState.Retrying)
                        && value.NextAttemptAt < now
                    || value.State == DeliveryState.Claimed && value.LeaseUntil < now) })
            .ToListAsync(cancellationToken);
        var projectById = projects.ToDictionary(value => value.Id);
        var all = new List<OverviewMonitor>(http.Count + push.Count);

        foreach (var monitor in http)
        {
            checks.TryGetValue(monitor.LatestResultId ?? Guid.Empty, out var latest);
            checks.TryGetValue(monitor.LatestSuccessId ?? Guid.Empty, out var success);
            httpIncidents.TryGetValue(monitor.Id, out var incident);
            var state = State(monitor.State);
            all.Add(new OverviewMonitor(projectById[monitor.ProjectId].Key, "http", monitor.Key,
                monitor.Name, state, null, Overdue(monitor.State, monitor.NextCheckAt, now),
                monitor.State == MonitorState.Paused ? null : monitor.NextCheckAt,
                success?.CompletedAt, latest?.EvaluationGeneration == monitor.EvaluationGeneration
                    ? latest.CompletedAt : null,
                latest?.EvaluationGeneration == monitor.EvaluationGeneration
                    ? latest.Outcome == CheckOutcome.Success ? "success" : "failure" : null,
                latest?.EvaluationGeneration == monitor.EvaluationGeneration ? latest.FailureReason : null,
                incident?.Id, incident?.BeganAt, incident?.LatestReason,
                EffectiveEnd(active, monitor.ProjectId, "http", monitor.Id)));
        }
        foreach (var monitor in push)
        {
            reports.TryGetValue(monitor.LatestReportId ?? Guid.Empty, out var latest);
            reports.TryGetValue(monitor.LatestSuccessId ?? Guid.Empty, out var success);
            pushIncidents.TryGetValue(monitor.Id, out var incident);
            var applicable = latest is not null
                && latest.EvaluationGeneration == monitor.EvaluationGeneration
                && latest.Sequence == monitor.LastAppliedSequence
                && monitor.State != MonitorState.Untested;
            all.Add(new OverviewMonitor(projectById[monitor.ProjectId].Key, "push", monitor.Key,
                monitor.Name, State(monitor.State), monitor.Mode == PushMonitorMode.JobCompletion
                    ? "job_completion" : "state_report",
                Overdue(monitor.State, monitor.NextDeadlineAt, now),
                monitor.State == MonitorState.Paused ? null : monitor.NextDeadlineAt,
                success?.ObservedAt, applicable ? latest!.ObservedAt : null,
                applicable ? latest!.Outcome == ReportOutcome.Success ? "success" : "failure" : null,
                applicable ? latest!.Outcome == ReportOutcome.Success ? null
                    : latest.IsDeadlineObservation ? "report_missing" : "reported_failure" : null,
                incident?.Id, incident?.BeganAt, incident?.LatestReason,
                EffectiveEnd(active, monitor.ProjectId, "push", monitor.Id)));
        }

        var deliveryByProject = deliveries.GroupBy(value => value.ProjectKey)
            .ToDictionary(group => group.Key, group => Delivery(group
                .Select(row => (row.State, row.Count, row.Oldest, row.Overdue))));
        var monitorsByProject = all.GroupBy(value => value.ProjectKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var overviewProjects = projects.Select(project =>
        {
            var members = monitorsByProject.GetValueOrDefault(project.Key) ?? [];
            active.TryGetValue(("project", project.Id), out var maintenanceEnd);
            return new OverviewProject(project.Key, project.Name, Counts(members),
                deliveryByProject.GetValueOrDefault(project.Key) ?? new OverviewDelivery(0, 0, 0, 0, 0, null),
                maintenanceEnd == default ? null : maintenanceEnd);
        }).OrderByDescending(value => ProjectPriority(value,
            monitorsByProject.GetValueOrDefault(value.Key) ?? []))
            .ThenBy(value => value.Key, StringComparer.Ordinal).ToArray();
        var attention = all.Where(value => value.State != "healthy" || value.Overdue)
            .OrderBy(Priority).ThenBy(value => value.ProjectKey, StringComparer.Ordinal)
            .ThenBy(value => value.Type, StringComparer.Ordinal)
            .ThenBy(value => value.Key, StringComparer.Ordinal).ToArray();
        return new InstanceOverview(now, Counts(all),
            Delivery(deliveries.Select(row => (row.State, row.Count, row.Oldest, row.Overdue))),
            attention, overviewProjects);
    }

    private static string State(MonitorState value) => value.ToString().ToLowerInvariant();
    private static bool Overdue(MonitorState state, DateTimeOffset? due, DateTimeOffset now) =>
        state != MonitorState.Paused && due < now;
    private static OverviewCounts Counts(IReadOnlyCollection<OverviewMonitor> monitors) => new(
        monitors.Count, monitors.Count(value => value.State == "healthy"),
        monitors.Count(value => value.State == "failing"),
        monitors.Count(value => value.State == "untested"),
        monitors.Count(value => value.State == "paused"),
        monitors.Count(value => value.Overdue));
    private static OverviewDelivery Delivery(IEnumerable<(DeliveryState State, int Count,
        DateTimeOffset Oldest, int Overdue)> rows)
    {
        var values = rows.ToArray();
        var pending = values.Where(value => value.State is DeliveryState.Queued
            or DeliveryState.Claimed or DeliveryState.Retrying).ToArray();
        return new(pending.Sum(value => value.Count), pending.Sum(value => value.Overdue),
            values.Where(value => value.State == DeliveryState.Retrying).Sum(value => value.Count),
            values.Where(value => value.State == DeliveryState.TerminalFailure).Sum(value => value.Count),
            values.Where(value => value.State == DeliveryState.Accepted).Sum(value => value.Count),
            pending.Length == 0 ? null : pending.Min(value => value.Oldest));
    }
    private static DateTimeOffset? EffectiveEnd(
        IReadOnlyDictionary<(string ScopeType, Guid ScopeId), DateTimeOffset> windows,
        Guid projectId, string type, Guid monitorId)
    {
        var hasProject = windows.TryGetValue(("project", projectId), out var project);
        var hasMonitor = windows.TryGetValue((type, monitorId), out var monitor);
        return hasProject && hasMonitor ? project > monitor ? project : monitor
            : hasProject ? project : hasMonitor ? monitor : null;
    }
    private static int Priority(OverviewMonitor value) => value.IncidentId is not null ? 0
        : value.State == "failing" ? 1 : value.Overdue ? 2
        : value.State == "untested" ? 3 : 4;
    private static int ProjectPriority(OverviewProject project,
        IReadOnlyCollection<OverviewMonitor> monitors) =>
        monitors.Any(value => value.IncidentId is not null) ? 4
        : project.Counts.Failing > 0 || project.Delivery.TerminalFailure > 0 ? 3
        : project.Counts.Overdue > 0 || project.Delivery.Pending > 0 ? 2
        : project.Counts.Untested > 0 ? 1 : 0;
}
