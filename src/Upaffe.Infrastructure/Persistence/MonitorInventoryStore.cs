using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;

namespace Upaffe.Infrastructure.Persistence;

/// <summary>Two bounded read queries: a filtered count and one ordered page.</summary>
public sealed class MonitorInventoryStore(UpaffeDbContext context) : IMonitorInventoryStore
{
    public async Task<MonitorInventoryPage> ReadAsync(MonitorInventoryFilter filter,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var http = from monitor in context.HttpMonitors.AsNoTracking()
                   join project in context.Projects.AsNoTracking() on monitor.ProjectId equals project.Id
                   where monitor.DeletedAt == null && project.DeletedAt == null
                   select new InventoryRow
                   {
                       ProjectKey = project.Key, ProjectName = project.Name,
                       Id = monitor.Id, Type = "http", Key = monitor.Key, Name = monitor.Name,
                       Purpose = monitor.Purpose, Mode = null, TargetUrl = monitor.TargetUrl,
                       IntervalSeconds = monitor.IntervalSeconds, ToleranceSeconds = null,
                       State = monitor.State, NextDueAt = monitor.NextCheckAt,
                       HasIncident = context.Incidents.Any(value => value.MonitorId == monitor.Id && value.ResolvedAt == null),
                       LatestObservationAt = context.HttpChecks.Where(value => value.Id == monitor.LatestResultId)
                           .Select(value => value.CompletedAt).FirstOrDefault(),
                       LastSuccessAt = context.HttpChecks.Where(value => value.Id == monitor.LatestSuccessId)
                           .Select(value => value.CompletedAt).FirstOrDefault(),
                       MaintenanceUntil = context.MaintenanceWindows
                           .Where(value => value.EndedAt == null && value.EndsAt > now
                               && (value.ScopeType == "project" && value.ScopeId == project.Id
                                   || value.ScopeType == "http" && value.ScopeId == monitor.Id))
                           .Select(value => (DateTimeOffset?)value.EndsAt).Max(),
                   };
        var push = from monitor in context.PushMonitors.AsNoTracking()
                   join project in context.Projects.AsNoTracking() on monitor.ProjectId equals project.Id
                   where monitor.DeletedAt == null && project.DeletedAt == null
                   select new InventoryRow
                   {
                       ProjectKey = project.Key, ProjectName = project.Name,
                       Id = monitor.Id, Type = "push", Key = monitor.Key, Name = monitor.Name,
                       Purpose = monitor.Purpose,
                       Mode = monitor.Mode == PushMonitorMode.JobCompletion ? "job_completion" : "state_report",
                       TargetUrl = null,
                       IntervalSeconds = monitor.IntervalSeconds, ToleranceSeconds = monitor.ToleranceSeconds,
                       State = monitor.State, NextDueAt = monitor.NextDeadlineAt,
                       HasIncident = context.PushIncidents.Any(value => value.MonitorId == monitor.Id && value.ResolvedAt == null),
                       LatestObservationAt = context.PushReports.Where(value => value.Id == monitor.LatestReportId)
                           .Select(value => (DateTimeOffset?)value.ObservedAt).FirstOrDefault(),
                       LastSuccessAt = context.PushReports.Where(value => value.Id == monitor.LatestSuccessId)
                           .Select(value => (DateTimeOffset?)value.ObservedAt).FirstOrDefault(),
                       MaintenanceUntil = context.MaintenanceWindows
                           .Where(value => value.EndedAt == null && value.EndsAt > now
                               && (value.ScopeType == "project" && value.ScopeId == project.Id
                                   || value.ScopeType == "push" && value.ScopeId == monitor.Id))
                           .Select(value => (DateTimeOffset?)value.EndsAt).Max(),
                   };

        IQueryable<InventoryRow> rows = filter.Type switch
        {
            "http" => http,
            "push" => push,
            _ => http.Concat(push),
        };
        if (filter.Project is not null)
            rows = rows.Where(value => value.ProjectKey == filter.Project);
        if (filter.State is not null)
        {
            var state = Enum.Parse<MonitorState>(filter.State, true);
            rows = rows.Where(value => value.State == state);
        }
        if (filter.Search is not null)
        {
            var pattern = $"%{filter.Search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            rows = rows.Where(value => EF.Functions.ILike(value.ProjectKey, pattern, "\\")
                || EF.Functions.ILike(value.ProjectName, pattern, "\\")
                || EF.Functions.ILike(value.Key, pattern, "\\")
                || EF.Functions.ILike(value.Name, pattern, "\\")
                || EF.Functions.ILike(value.Purpose ?? "", pattern, "\\")
                || EF.Functions.ILike(value.TargetUrl ?? "", pattern, "\\"));
        }

        var total = await rows.CountAsync(cancellationToken);
        var page = await rows
            .OrderBy(value => value.HasIncident ? 0 : value.State == MonitorState.Failing ? 1
                : value.State != MonitorState.Paused && value.NextDueAt < now ? 2
                : value.State == MonitorState.Untested ? 3
                : value.State == MonitorState.Paused ? 4 : 5)
            .ThenBy(value => value.ProjectKey).ThenBy(value => value.Type).ThenBy(value => value.Key)
            .Skip(filter.Offset).Take(filter.Limit).ToListAsync(cancellationToken);
        var items = page.Select(value => new MonitorInventoryItem(value.ProjectKey, value.ProjectName,
            value.Id, value.Type, value.Key, value.Name, value.Purpose, value.Mode, value.TargetUrl,
            value.IntervalSeconds, value.ToleranceSeconds, value.State.ToString().ToLowerInvariant(),
            value.State != MonitorState.Paused && value.NextDueAt < now, value.HasIncident,
            value.LatestObservationAt, value.LastSuccessAt, value.NextDueAt,
            value.MaintenanceUntil)).ToArray();
        return new(now, total, filter.Limit, filter.Offset,
            filter.Offset + items.Length < total, items);
    }

    private sealed class InventoryRow
    {
        public string ProjectKey { get; init; } = string.Empty;
        public string ProjectName { get; init; } = string.Empty;
        public Guid Id { get; init; }
        public string Type { get; init; } = string.Empty;
        public string Key { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Purpose { get; init; }
        public string? Mode { get; init; }
        public string? TargetUrl { get; init; }
        public int IntervalSeconds { get; init; }
        public int? ToleranceSeconds { get; init; }
        public MonitorState State { get; init; }
        public DateTimeOffset? NextDueAt { get; init; }
        public bool HasIncident { get; init; }
        public DateTimeOffset? LatestObservationAt { get; init; }
        public DateTimeOffset? LastSuccessAt { get; init; }
        public DateTimeOffset? MaintenanceUntil { get; init; }
    }
}
