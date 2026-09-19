using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Notifications;
using Upaffe.Domain.Projects;

namespace Upaffe.Application.Notifications;

public sealed class MaintenanceActs(IMaintenanceStore store, TimeProvider clock)
{
    public async Task<MaintenanceSnapshot> ReadAsync(Identity identity, string scopeType,
        string? projectKey, string? monitorKey, CancellationToken cancellationToken)
    {
        _ = identity;
        var keys = ValidKeys(scopeType, projectKey, monitorKey);
        return await store.ReadAsync(scopeType, keys.ProjectKey, keys.MonitorKey,
            clock.GetUtcNow(), cancellationToken)
            ?? throw Refusal.NotFound("No such live maintenance scope.");
    }

    public async Task<MaintenanceSnapshot> StartAsync(Identity identity, string scopeType,
        string? projectKey, string? monitorKey, long version, int durationSeconds,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var keys = ValidKeys(scopeType, projectKey, monitorKey);
        if (version < 0) throw Validation("version", "Maintenance version must be nonnegative.");
        var duration = TimeSpan.FromSeconds(durationSeconds);
        try { MaintenanceWindow.ValidateDuration(duration); }
        catch (ArgumentException ex) { throw Validation("duration_seconds", ex.Message); }
        return Changed(await store.StartAsync(scopeType, keys.ProjectKey, keys.MonitorKey,
            version, duration, clock.GetUtcNow(), cancellationToken));
    }

    public async Task<MaintenanceSnapshot> EndAsync(Identity identity, string scopeType,
        string? projectKey, string? monitorKey, long version,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var keys = ValidKeys(scopeType, projectKey, monitorKey);
        if (version < 0) throw Validation("version", "Maintenance version must be nonnegative.");
        return Changed(await store.EndAsync(scopeType, keys.ProjectKey, keys.MonitorKey,
            version, clock.GetUtcNow(), cancellationToken));
    }

    private static (string ProjectKey, string? MonitorKey) ValidKeys(string scopeType,
        string? projectKey, string? monitorKey)
    {
        try
        {
            var project = Project.ValidateKey(projectKey ?? string.Empty);
            var monitor = scopeType switch
            {
                "project" => null,
                "http" => HttpMonitor.ValidateKey(monitorKey ?? string.Empty),
                "push" => PushMonitor.ValidateKey(monitorKey ?? string.Empty),
                _ => throw new ArgumentException("Unknown maintenance scope."),
            };
            return (project, monitor);
        }
        catch (ArgumentException ex) { throw Validation("scope", ex.Message); }
    }

    private static MaintenanceSnapshot Changed(MaintenanceMutationResult result) =>
        result.Outcome switch
        {
            MaintenanceMutation.Changed when result.Snapshot is not null => result.Snapshot,
            MaintenanceMutation.Missing => throw Refusal.NotFound("No such maintenance scope."),
            MaintenanceMutation.Deleted => throw Refusal.Conflict("A deleted scope cannot enter maintenance."),
            MaintenanceMutation.VersionConflict => throw Refusal.Conflict("Maintenance changed after the supplied version was read."),
            MaintenanceMutation.NoActive => throw Refusal.Conflict("No active maintenance window exists for this scope."),
            _ => throw new InvalidOperationException("Invalid maintenance store result."),
        };

    private static Refusal Validation(string field, string message) =>
        Refusal.Validation(new Dictionary<string, string[]> { [field] = [message] });
}
