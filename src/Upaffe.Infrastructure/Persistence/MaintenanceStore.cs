using Microsoft.EntityFrameworkCore;
using Npgsql;
using Upaffe.Application.Ports;
using Upaffe.Domain.Notifications;

namespace Upaffe.Infrastructure.Persistence;

public sealed class MaintenanceStore(UpaffeDbContext context) : IMaintenanceStore
{
    public async Task<MaintenanceSnapshot?> ReadAsync(string scopeType, string projectKey,
        string? monitorKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var scope = await ResolveAsync(scopeType, projectKey, monitorKey, cancellationToken);
        return scope is { Deleted: false } ? await SnapshotAsync(scopeType, projectKey,
            monitorKey, scope.ProjectId, scope.ScopeId, now, cancellationToken) : null;
    }

    public Task<MaintenanceMutationResult> StartAsync(string scopeType, string projectKey,
        string? monitorKey, long version, TimeSpan duration, DateTimeOffset now,
        CancellationToken cancellationToken) => MutateAsync(scopeType, projectKey,
            monitorKey, version, duration, now, false, cancellationToken);

    public Task<MaintenanceMutationResult> EndAsync(string scopeType, string projectKey,
        string? monitorKey, long version, DateTimeOffset now,
        CancellationToken cancellationToken) => MutateAsync(scopeType, projectKey,
            monitorKey, version, null, now, true, cancellationToken);

    private async Task<MaintenanceMutationResult> MutateAsync(string scopeType,
        string projectKey, string? monitorKey, long version, TimeSpan? duration,
        DateTimeOffset now, bool end, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var scope = await ResolveAsync(scopeType, projectKey, monitorKey, cancellationToken);
        if (scope is null) return new(MaintenanceMutation.Missing);
        if (scope.Deleted) return new(MaintenanceMutation.Deleted);
        var latest = await LatestAsync(scopeType, scope.ScopeId, cancellationToken);
        if ((latest?.Version ?? 0) != version) return new(MaintenanceMutation.VersionConflict);
        if (end)
        {
            if (latest is null || !latest.IsActive(now)) return new(MaintenanceMutation.NoActive);
            latest.End(now);
        }
        else if (latest is not null && latest.IsActive(now))
            latest.Extend(now, duration!.Value);
        else
            context.MaintenanceWindows.Add(MaintenanceWindow.Start(scopeType,
                scope.ScopeId, version + 1, now, duration!.Value));
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return new(MaintenanceMutation.VersionConflict); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        { return new(MaintenanceMutation.VersionConflict); }
        await transaction.CommitAsync(cancellationToken);
        return new(MaintenanceMutation.Changed, await SnapshotAsync(scopeType,
            projectKey, monitorKey, scope.ProjectId, scope.ScopeId, now, cancellationToken));
    }

    private async Task<MaintenanceSnapshot> SnapshotAsync(string scopeType,
        string projectKey, string? monitorKey, Guid projectId, Guid scopeId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var direct = await LatestAsync(scopeType, scopeId, cancellationToken);
        var parent = scopeType == "project" ? null
            : await LatestAsync("project", projectId, cancellationToken);
        var directActive = direct?.IsActive(now) == true;
        var parentActive = parent?.IsActive(now) == true;
        var activeScopes = new List<string>();
        if (parentActive) activeScopes.Add("project");
        if (directActive) activeScopes.Add(scopeType);
        var ends = new[] { directActive ? direct!.EndsAt : (DateTimeOffset?)null,
            parentActive ? parent!.EndsAt : null }.OfType<DateTimeOffset>().ToArray();
        return new(scopeType, projectKey, monitorKey, direct?.Version ?? 0,
            direct?.StartedAt, direct?.EndsAt, direct?.EndedAt,
            directActive, ends.Length > 0, ends.Length == 0 ? null : ends.Max(),
            activeScopes);
    }

    private Task<MaintenanceWindow?> LatestAsync(string scopeType, Guid scopeId,
        CancellationToken cancellationToken) => context.MaintenanceWindows
            .Where(value => value.ScopeType == scopeType && value.ScopeId == scopeId)
            .OrderByDescending(value => value.Version)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<Scope?> ResolveAsync(string scopeType, string projectKey,
        string? monitorKey, CancellationToken cancellationToken)
    {
        var project = await context.Projects.AsNoTracking().SingleOrDefaultAsync(
            value => value.Key == projectKey, cancellationToken);
        if (project is null) return null;
        if (scopeType == "project")
            return new(project.Id, project.Id, project.DeletedAt is not null);
        if (scopeType == "http")
        {
            var monitor = await context.HttpMonitors.AsNoTracking().SingleOrDefaultAsync(
                value => value.ProjectId == project.Id && value.Key == monitorKey, cancellationToken);
            return monitor is null ? null : new(project.Id, monitor.Id,
                project.DeletedAt is not null || monitor.DeletedAt is not null);
        }
        var push = await context.PushMonitors.AsNoTracking().SingleOrDefaultAsync(
            value => value.ProjectId == project.Id && value.Key == monitorKey, cancellationToken);
        return push is null ? null : new(project.Id, push.Id,
            project.DeletedAt is not null || push.DeletedAt is not null);
    }

    private sealed record Scope(Guid ProjectId, Guid ScopeId, bool Deleted);
}
