using Upaffe.Domain.Monitoring;

namespace Upaffe.Application.Ports;

public sealed record HttpHeaderSnapshot(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record HttpMonitorSnapshot(
    Guid Id,
    string ProjectKey,
    string Key,
    string Name,
    string TargetUrl,
    bool HasTargetQuery,
    int ExpectedStatusCode,
    TextCondition TextCondition,
    string? TextFragment,
    int IntervalSeconds,
    int TimeoutSeconds,
    int FailureThreshold,
    string? Instruction,
    string? RunbookUrl,
    MonitorState State,
    int ConsecutiveFailures,
    DateTimeOffset? NextCheckAt,
    Guid? LatestResultId,
    Guid? LatestSuccessId,
    Guid? OpenIncidentId,
    IReadOnlyList<HttpHeaderSnapshot> Headers,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PausedAt,
    DateTimeOffset? DeletedAt);

public sealed record HttpMonitorDefinition(
    string Key,
    string Name,
    string TargetUrl,
    int ExpectedStatusCode,
    TextCondition TextCondition,
    string? TextFragment,
    int IntervalSeconds,
    int TimeoutSeconds,
    int FailureThreshold,
    string? Instruction,
    string? RunbookUrl);

public sealed record HttpMonitorChange(
    string Name,
    string? TargetUrl,
    int ExpectedStatusCode,
    TextCondition TextCondition,
    string? TextFragment,
    int IntervalSeconds,
    int TimeoutSeconds,
    int FailureThreshold,
    string? Instruction,
    string? RunbookUrl);

public sealed record HttpHeaderValue(string Name, string Value);

public enum HttpMonitorMutation
{
    Changed,
    Unchanged,
    Missing,
    ProjectMissing,
    ProjectDeleted,
    Conflict,
    VersionConflict,
    Deleted,
    Paused,
}

public sealed record HttpMonitorMutationResult(
    HttpMonitorMutation Outcome,
    HttpMonitorSnapshot? Monitor = null);

public sealed record StartedHttpTest(
    HttpMonitorMutation Outcome,
    Guid? CheckId = null,
    HttpExecutionRequest? Request = null);

public sealed record CompletedHttpTest(
    Guid CheckId,
    bool AppliedToCurrentState,
    HttpExecutionResult Result,
    HttpMonitorSnapshot Monitor);

public interface IHttpMonitorStore
{
    Task<HttpMonitorMutationResult> CreateAsync(
        string projectKey,
        HttpMonitorDefinition definition,
        IReadOnlyList<HttpHeaderValue> headers,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<HttpMonitorSnapshot?> GetAsync(
        string projectKey,
        string monitorKey,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<HttpMonitorSnapshot>?> ListAsync(
        string projectKey,
        CancellationToken cancellationToken);

    Task<HttpMonitorMutationResult> UpdateAsync(
        string projectKey,
        string monitorKey,
        HttpMonitorChange change,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<HttpMonitorMutationResult> PauseAsync(
        string projectKey,
        string monitorKey,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<HttpMonitorMutationResult> ResumeAsync(
        string projectKey,
        string monitorKey,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<HttpMonitorMutationResult> RemoveAsync(
        string projectKey,
        string monitorKey,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<HttpMonitorMutationResult> SetHeaderAsync(
        string projectKey,
        string monitorKey,
        string name,
        string value,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<HttpMonitorMutationResult> RemoveHeaderAsync(
        string projectKey,
        string monitorKey,
        string name,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<StartedHttpTest> StartTestAsync(
        string projectKey,
        string monitorKey,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<CompletedHttpTest> CompleteTestAsync(
        Guid checkId,
        HttpExecutionResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
