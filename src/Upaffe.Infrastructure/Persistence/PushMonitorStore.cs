using Microsoft.EntityFrameworkCore;
using Npgsql;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;

namespace Upaffe.Infrastructure.Persistence;

public sealed class PushMonitorStore(UpaffeDbContext context) : IPushMonitorStore
{
    public async Task<PushMonitorMutationResult> CreateAsync(string projectKey, PushMonitorDefinition definition, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var project = await context.Projects.SingleOrDefaultAsync(value => value.Key == projectKey, cancellationToken);
        if (project is null) return new(PushMonitorMutation.ProjectMissing);
        if (project.DeletedAt is not null) return new(PushMonitorMutation.ProjectDeleted);
        if (await context.HttpMonitors.AnyAsync(value => value.ProjectId == project.Id && value.Key == definition.Key, cancellationToken))
            return new(PushMonitorMutation.Conflict);

        var monitor = Create(project.Id, definition, now);
        context.Add(monitor);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new(PushMonitorMutation.Changed, await SnapshotAsync(monitor, projectKey, cancellationToken));
        }
        catch (DbUpdateException exception) when (IsKeyConflict(exception))
        {
            context.ChangeTracker.Clear();
            var existing = await context.PushMonitors.SingleOrDefaultAsync(value => value.ProjectId == project.Id && value.Key == definition.Key, cancellationToken);
            if (existing is null || existing.DeletedAt is not null || !Same(existing, definition)) return new(PushMonitorMutation.Conflict);
            return new(PushMonitorMutation.Unchanged, await SnapshotAsync(existing, projectKey, cancellationToken));
        }
    }

    public async Task<PushMonitorSnapshot?> GetAsync(string projectKey, string monitorKey, CancellationToken cancellationToken)
    {
        var found = await FindLiveAsync(projectKey, monitorKey, cancellationToken);
        return found is null ? null : await SnapshotAsync(found.Value.Monitor, projectKey, cancellationToken);
    }

    public async Task<IReadOnlyList<PushMonitorSnapshot>?> ListAsync(string projectKey, CancellationToken cancellationToken)
    {
        var project = await context.Projects.AsNoTracking().SingleOrDefaultAsync(value => value.Key == projectKey && value.DeletedAt == null, cancellationToken);
        if (project is null) return null;
        var values = await context.PushMonitors.AsNoTracking().Where(value => value.ProjectId == project.Id && value.DeletedAt == null)
            .OrderBy(value => value.Key).ToListAsync(cancellationToken);
        var result = new List<PushMonitorSnapshot>(values.Count);
        foreach (var value in values) result.Add(await SnapshotAsync(value, projectKey, cancellationToken));
        return result;
    }

    public async Task<PushMonitorMutationResult> UpdateAsync(string projectKey, string monitorKey, PushMonitorChange change, long expectedVersion, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var found = await FindForChangeAsync(projectKey, monitorKey, expectedVersion, cancellationToken);
        if (found.Result is not null) return found.Result;
        found.Monitor!.ChangeConfiguration(change.Name, change.IntervalSeconds, change.ToleranceSeconds, change.Instruction, change.RunbookUrl, now);
        return await SaveAsync(found.Monitor, projectKey, cancellationToken);
    }

    public Task<PushMonitorMutationResult> PauseAsync(string projectKey, string monitorKey, long expectedVersion, DateTimeOffset now, CancellationToken cancellationToken) =>
        ChangeStateAsync(projectKey, monitorKey, expectedVersion, now, true, cancellationToken);

    public Task<PushMonitorMutationResult> ResumeAsync(string projectKey, string monitorKey, long expectedVersion, DateTimeOffset now, CancellationToken cancellationToken) =>
        ChangeStateAsync(projectKey, monitorKey, expectedVersion, now, false, cancellationToken);

    public async Task<PushMonitorMutationResult> RemoveAsync(string projectKey, string monitorKey, long expectedVersion, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var project = await context.Projects.SingleOrDefaultAsync(value => value.Key == projectKey, cancellationToken);
        if (project is null) return new(PushMonitorMutation.ProjectMissing);
        var monitor = await context.PushMonitors.SingleOrDefaultAsync(value => value.ProjectId == project.Id && value.Key == monitorKey, cancellationToken);
        if (monitor is null) return new(PushMonitorMutation.Missing);
        if (monitor.Version != expectedVersion) return new(PushMonitorMutation.VersionConflict);
        if (monitor.DeletedAt is not null) return new(PushMonitorMutation.Unchanged, await SnapshotAsync(monitor, projectKey, cancellationToken));
        monitor.Remove(now);
        var credential = await context.ReportingCredentials.SingleOrDefaultAsync(value => value.MonitorId == monitor.Id, cancellationToken);
        credential?.Revoke(now);
        return await SaveAsync(monitor, projectKey, cancellationToken);
    }

    private async Task<PushMonitorMutationResult> ChangeStateAsync(string projectKey, string monitorKey, long expectedVersion, DateTimeOffset now, bool pause, CancellationToken cancellationToken)
    {
        var found = await FindForChangeAsync(projectKey, monitorKey, expectedVersion, cancellationToken);
        if (found.Result is not null) return found.Result;
        var before = found.Monitor!.Version;
        if (pause) found.Monitor.Pause(now); else found.Monitor.Resume(now);
        return before == found.Monitor.Version
            ? new(PushMonitorMutation.Unchanged, await SnapshotAsync(found.Monitor, projectKey, cancellationToken))
            : await SaveAsync(found.Monitor, projectKey, cancellationToken);
    }

    private async Task<(PushMonitor? Monitor, PushMonitorMutationResult? Result)> FindForChangeAsync(string projectKey, string monitorKey, long version, CancellationToken cancellationToken)
    {
        var project = await context.Projects.SingleOrDefaultAsync(value => value.Key == projectKey, cancellationToken);
        if (project is null) return (null, new(PushMonitorMutation.ProjectMissing));
        if (project.DeletedAt is not null) return (null, new(PushMonitorMutation.ProjectDeleted));
        var monitor = await context.PushMonitors.SingleOrDefaultAsync(value => value.ProjectId == project.Id && value.Key == monitorKey, cancellationToken);
        if (monitor is null) return (null, new(PushMonitorMutation.Missing));
        if (monitor.Version != version) return (null, new(PushMonitorMutation.VersionConflict));
        if (monitor.DeletedAt is not null) return (null, new(PushMonitorMutation.Deleted));
        return (monitor, null);
    }

    private async Task<(Guid ProjectId, PushMonitor Monitor)?> FindLiveAsync(string projectKey, string monitorKey, CancellationToken cancellationToken)
    {
        var project = await context.Projects.AsNoTracking().SingleOrDefaultAsync(value => value.Key == projectKey && value.DeletedAt == null, cancellationToken);
        if (project is null) return null;
        var monitor = await context.PushMonitors.AsNoTracking().SingleOrDefaultAsync(value => value.ProjectId == project.Id && value.Key == monitorKey && value.DeletedAt == null, cancellationToken);
        return monitor is null ? null : (project.Id, monitor);
    }

    private async Task<PushMonitorMutationResult> SaveAsync(PushMonitor monitor, string projectKey, CancellationToken cancellationToken)
    {
        try { await context.SaveChangesAsync(cancellationToken); return new(PushMonitorMutation.Changed, await SnapshotAsync(monitor, projectKey, cancellationToken)); }
        catch (DbUpdateConcurrencyException) { return new(PushMonitorMutation.VersionConflict); }
    }

    private async Task<PushMonitorSnapshot> SnapshotAsync(PushMonitor monitor, string projectKey, CancellationToken cancellationToken)
    {
        var incident = await context.PushIncidents.AsNoTracking().Where(value => value.MonitorId == monitor.Id && value.ResolvedAt == null)
            .Select(value => (Guid?)value.Id).SingleOrDefaultAsync(cancellationToken);
        var hasCredential = await context.ReportingCredentials.AsNoTracking().AnyAsync(value => value.MonitorId == monitor.Id && value.RevokedAt == null, cancellationToken);
        return new(monitor.Id, projectKey, monitor.Key, monitor.Name, monitor.Mode, monitor.IntervalSeconds, monitor.ToleranceSeconds,
            monitor.Instruction, monitor.RunbookUrl, monitor.State, monitor.LastReceivedAt, monitor.NextDeadlineAt,
            monitor.LatestReportId, monitor.LatestSuccessId, incident, hasCredential, monitor.Version, monitor.CreatedAt,
            monitor.UpdatedAt, monitor.PausedAt, monitor.DeletedAt);
    }

    private static PushMonitor Create(Guid projectId, PushMonitorDefinition value, DateTimeOffset now) =>
        PushMonitor.Create(projectId, value.Key, value.Name, value.Mode, value.IntervalSeconds, value.ToleranceSeconds, value.Instruction, value.RunbookUrl, now);

    private static bool Same(PushMonitor monitor, PushMonitorDefinition value) => monitor.Name == value.Name && monitor.Mode == value.Mode
        && monitor.IntervalSeconds == value.IntervalSeconds && monitor.ToleranceSeconds == value.ToleranceSeconds
        && monitor.Instruction == value.Instruction && monitor.RunbookUrl == value.RunbookUrl;

    private static bool IsKeyConflict(DbUpdateException exception) => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres
        && postgres.ConstraintName == "push_monitor_project_key";
}
