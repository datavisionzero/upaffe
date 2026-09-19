namespace Upaffe.Application.Ports;

public sealed record MaintenanceSnapshot(
    string ScopeType, string ProjectKey, string? MonitorKey, long Version,
    DateTimeOffset? StartedAt, DateTimeOffset? EndsAt, DateTimeOffset? EndedAt,
    bool DirectActive, bool EffectiveActive, DateTimeOffset? EffectiveEndsAt,
    IReadOnlyList<string> ActiveScopes);

public enum MaintenanceMutation { Changed, Missing, Deleted, VersionConflict, NoActive }
public sealed record MaintenanceMutationResult(MaintenanceMutation Outcome,
    MaintenanceSnapshot? Snapshot = null);

public interface IMaintenanceStore
{
    Task<MaintenanceSnapshot?> ReadAsync(string scopeType, string projectKey,
        string? monitorKey, DateTimeOffset now, CancellationToken cancellationToken);
    Task<MaintenanceMutationResult> StartAsync(string scopeType, string projectKey,
        string? monitorKey, long version, TimeSpan duration, DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<MaintenanceMutationResult> EndAsync(string scopeType, string projectKey,
        string? monitorKey, long version, DateTimeOffset now,
        CancellationToken cancellationToken);
}
