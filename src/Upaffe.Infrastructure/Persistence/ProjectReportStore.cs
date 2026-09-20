using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Notifications;

namespace Upaffe.Infrastructure.Persistence;

/// <summary>A bounded set of read-only queries, independent of monitor count.</summary>
public sealed class ProjectReportStore(UpaffeDbContext context, IEmailStatusStore emailStatus)
    : IProjectReportStore
{
    public async Task<ProjectReport?> ReadAsync(string projectKey, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var project = await context.Projects.AsNoTracking().SingleOrDefaultAsync(
            value => value.Key == projectKey && value.DeletedAt == null, cancellationToken);
        if (project is null) return null;

        var http = await context.HttpMonitors.AsNoTracking()
            .Where(value => value.ProjectId == project.Id && value.DeletedAt == null)
            .ToListAsync(cancellationToken);
        var push = await context.PushMonitors.AsNoTracking()
            .Where(value => value.ProjectId == project.Id && value.DeletedAt == null)
            .ToListAsync(cancellationToken);
        var httpIds = http.Select(value => value.Id).ToArray();
        var pushIds = push.Select(value => value.Id).ToArray();
        var checkIds = http.SelectMany(value => new[] { value.LatestResultId, value.LatestSuccessId })
            .Where(value => value.HasValue).Select(value => value!.Value).Distinct().ToArray();
        var reportIds = push.Select(value => value.LatestSuccessId)
            .Where(value => value.HasValue).Select(value => value!.Value).Distinct().ToArray();

        var checks = await context.HttpChecks.AsNoTracking()
            .Where(value => checkIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var reports = await context.PushReports.AsNoTracking()
            .Where(value => reportIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var appliedReports = await context.PushReports.AsNoTracking()
            .Where(value => pushIds.Contains(value.MonitorId)
                && context.PushMonitors.Any(monitor => monitor.Id == value.MonitorId
                    && monitor.EvaluationGeneration == value.EvaluationGeneration
                    && monitor.LastAppliedSequence == value.Sequence))
            .ToDictionaryAsync(value => value.MonitorId, cancellationToken);
        var httpIncidents = await context.Incidents.AsNoTracking()
            .Where(value => httpIds.Contains(value.MonitorId) && value.ResolvedAt == null)
            .ToDictionaryAsync(value => value.MonitorId, cancellationToken);
        var pushIncidents = await context.PushIncidents.AsNoTracking()
            .Where(value => pushIds.Contains(value.MonitorId) && value.ResolvedAt == null)
            .ToDictionaryAsync(value => value.MonitorId, cancellationToken);
        var scopeIds = httpIds.Concat(pushIds).Append(project.Id).ToArray();
        var windows = await context.MaintenanceWindows.AsNoTracking()
            .Where(value => scopeIds.Contains(value.ScopeId)
                && value.EndedAt == null && value.EndsAt > now)
            .ToListAsync(cancellationToken);
        var active = windows.GroupBy(value => (value.ScopeType, value.ScopeId))
            .ToDictionary(group => group.Key, group => group.MaxBy(value => value.EndsAt)!);
        active.TryGetValue(("project", project.Id), out var projectWindow);
        var smtp = await context.EmailConfigurations.AsNoTracking()
            .Where(value => value.Id == EmailConfiguration.SingletonId)
            .Select(value => new { value.Host, value.Port, value.Security,
                value.SenderAddress, value.PublicBaseUrl,
                HasPassword = value.Password != null })
            .SingleOrDefaultAsync(cancellationToken);
        var delivery = await emailStatus.SummaryAsync(projectKey, cancellationToken);

        var all = new List<ReportMonitor>(http.Count + push.Count);
        foreach (var monitor in http)
        {
            active.TryGetValue(("http", monitor.Id), out var direct);
            checks.TryGetValue(monitor.LatestResultId ?? Guid.Empty, out var latest);
            checks.TryGetValue(monitor.LatestSuccessId ?? Guid.Empty, out var success);
            httpIncidents.TryGetValue(monitor.Id, out var incident);
            all.Add(new ReportMonitor("http", monitor.Id, monitor.Key, monitor.Name,
                State(monitor.State), null, monitor.Version, monitor.TargetUrl,
                monitor.ExpectedStatusCode, monitor.TextCondition.ToString().ToLowerInvariant(),
                monitor.IntervalSeconds, monitor.TimeoutSeconds, monitor.FailureThreshold,
                monitor.ConsecutiveFailures, null, null, monitor.NextCheckAt,
                Overdue(monitor.State, monitor.NextCheckAt, now),
                latest?.EvaluationGeneration == monitor.EvaluationGeneration
                    ? HttpResult(latest) : null,
                HttpResult(success), incident is null ? null : new ReportIncident(
                    incident.Id, incident.BeganAt, incident.OpenedAt,
                    Age(now, incident.BeganAt), incident.OriginalReason, incident.LatestReason),
                Window(direct), EffectiveEnd(direct, projectWindow),
                monitor.Instruction, monitor.RunbookUrl, monitor.Purpose));
        }
        foreach (var monitor in push)
        {
            active.TryGetValue(("push", monitor.Id), out var direct);
            appliedReports.TryGetValue(monitor.Id, out var latest);
            reports.TryGetValue(monitor.LatestSuccessId ?? Guid.Empty, out var success);
            pushIncidents.TryGetValue(monitor.Id, out var incident);
            all.Add(new ReportMonitor("push", monitor.Id, monitor.Key, monitor.Name,
                State(monitor.State), monitor.Mode == PushMonitorMode.JobCompletion
                    ? "job_completion" : "state_report", monitor.Version, null, null,
                null, monitor.IntervalSeconds, null, null, null,
                monitor.ToleranceSeconds, monitor.LastReceivedAt, monitor.NextDeadlineAt,
                Overdue(monitor.State, monitor.NextDeadlineAt, now),
                monitor.State == MonitorState.Untested ? null : PushResult(latest),
                PushResult(success), incident is null ? null : new ReportIncident(
                    incident.Id, incident.BeganAt, incident.OpenedAt,
                    Age(now, incident.BeganAt), incident.OriginalReason, incident.LatestReason),
                Window(direct), EffectiveEnd(direct, projectWindow),
                monitor.Instruction, monitor.RunbookUrl, monitor.Purpose));
        }

        var attention = all.Where(value => value.State != "healthy" || value.Overdue)
            .OrderBy(Priority).ThenBy(value => value.Type, StringComparer.Ordinal)
            .ThenBy(value => value.Key, StringComparer.Ordinal).ToArray();
        var healthy = all.Where(value => value.State == "healthy" && !value.Overdue)
            .OrderBy(value => value.Type, StringComparer.Ordinal)
            .ThenBy(value => value.Key, StringComparer.Ordinal)
            .Select(value => new ReportHealthyMonitor(value.Type, value.Id,
                value.Key, value.Name, value.Mode, value.LastSuccess?.ObservedAt,
                value.NextDueAt, value.DirectMaintenance,
                value.EffectiveMaintenanceUntil, value.Purpose)).ToArray();
        var counts = new ReportCounts(all.Count, http.Count, push.Count,
            all.Count(value => value.State == "healthy"),
            all.Count(value => value.State == "failing"),
            all.Count(value => value.State == "untested"),
            all.Count(value => value.State == "paused"));
        return new ProjectReport(now, new ReportProject(project.Id, project.Key,
            project.Name, project.Version, project.CreatedAt, project.UpdatedAt),
            counts, attention, healthy, Window(projectWindow),
            new ReportEmail(smtp?.Host is not null && smtp.Port is not null
                && smtp.SenderAddress is not null, smtp?.Host, smtp?.Port,
                smtp?.Security, smtp?.SenderAddress, smtp?.PublicBaseUrl,
                smtp?.HasPassword ?? false, project.Recipients, delivery));
    }

    private static string State(MonitorState state) => state.ToString().ToLowerInvariant();
    private static bool Overdue(MonitorState state, DateTimeOffset? due, DateTimeOffset now) =>
        state != MonitorState.Paused && due < now;
    private static long Age(DateTimeOffset now, DateTimeOffset began) =>
        Math.Max(0, (long)(now - began).TotalSeconds);
    private static ReportMaintenance? Window(MaintenanceWindow? window) =>
        window is null ? null : new(window.StartedAt, window.EndsAt);
    private static DateTimeOffset? EffectiveEnd(MaintenanceWindow? direct,
        MaintenanceWindow? project) => direct is null ? project?.EndsAt
        : project is null ? direct.EndsAt : direct.EndsAt > project.EndsAt
            ? direct.EndsAt : project.EndsAt;
    private static int Priority(ReportMonitor monitor) => monitor.Incident is not null ? 0
        : monitor.State == "failing" ? 1 : monitor.Overdue ? 2
        : monitor.State == "untested" ? 3 : 4;
    private static ReportResult? HttpResult(HttpCheck? check) => check is null ? null
        : new(check.Id, check.Outcome == CheckOutcome.Success ? "success" : "failure",
            check.FailureReason, check.CompletedAt ?? check.StartedAt, null);
    private static ReportResult? PushResult(PushReport? report) => report is null ? null
        : new(report.Id, report.Outcome == ReportOutcome.Success ? "success" : "failure",
            report.Outcome == ReportOutcome.Success ? null
                : report.IsDeadlineObservation ? "report_missing" : "reported_failure",
            report.ObservedAt, report.ReceivedAt);
}
