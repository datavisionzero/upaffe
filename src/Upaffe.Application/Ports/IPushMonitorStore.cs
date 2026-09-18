using Upaffe.Domain.Monitoring;

namespace Upaffe.Application.Ports;

public sealed record PushMonitorSnapshot(
    Guid Id,
    string ProjectKey,
    string Key,
    string Name,
    PushMonitorMode Mode,
    int IntervalSeconds,
    int ToleranceSeconds,
    string? Instruction,
    string? RunbookUrl,
    MonitorState State,
    DateTimeOffset? LastReceivedAt,
    DateTimeOffset? NextDeadlineAt,
    Guid? LatestReportId,
    Guid? LatestSuccessId,
    Guid? OpenIncidentId,
    bool HasReportingCredential,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PausedAt,
    DateTimeOffset? DeletedAt);

public sealed record PushMonitorDefinition(
    string Key,
    string Name,
    PushMonitorMode Mode,
    int IntervalSeconds,
    int ToleranceSeconds,
    string? Instruction,
    string? RunbookUrl);

public sealed record PushMonitorChange(
    string Name,
    int IntervalSeconds,
    int ToleranceSeconds,
    string? Instruction,
    string? RunbookUrl);

public enum PushMonitorMutation
{
    Changed,
    Unchanged,
    Missing,
    ProjectMissing,
    ProjectDeleted,
    Conflict,
    VersionConflict,
    Deleted,
}

public sealed record PushMonitorMutationResult(
    PushMonitorMutation Outcome,
    PushMonitorSnapshot? Monitor = null);

public interface IPushMonitorStore
{
    Task<PushMonitorMutationResult> CreateAsync(string projectKey, PushMonitorDefinition definition, DateTimeOffset now, CancellationToken cancellationToken);
    Task<PushMonitorSnapshot?> GetAsync(string projectKey, string monitorKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<PushMonitorSnapshot>?> ListAsync(string projectKey, CancellationToken cancellationToken);
    Task<PushMonitorMutationResult> UpdateAsync(string projectKey, string monitorKey, PushMonitorChange change, long expectedVersion, DateTimeOffset now, CancellationToken cancellationToken);
    Task<PushMonitorMutationResult> PauseAsync(string projectKey, string monitorKey, long expectedVersion, DateTimeOffset now, CancellationToken cancellationToken);
    Task<PushMonitorMutationResult> ResumeAsync(string projectKey, string monitorKey, long expectedVersion, DateTimeOffset now, CancellationToken cancellationToken);
    Task<PushMonitorMutationResult> RemoveAsync(string projectKey, string monitorKey, long expectedVersion, DateTimeOffset now, CancellationToken cancellationToken);
}
