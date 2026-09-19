using Upaffe.Domain.Monitoring;

namespace Upaffe.Application.Ports;

public sealed record HttpCheckHistoryItem(
    Guid Id,
    long Sequence,
    CheckTrigger Trigger,
    DateTimeOffset ScheduledFor,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    CheckOutcome Outcome,
    string? FailureReason,
    int? StatusCode,
    int? ResponseTimeMilliseconds,
    string? EffectiveUrl,
    bool? AppliedToCurrentState);

public sealed record HttpCheckHistoryPage(
    IReadOnlyList<HttpCheckHistoryItem> Items,
    long? NextBeforeSequence);

public sealed record IncidentHistoryItem(
    Guid Id,
    Guid FirstFailureCheckId,
    Guid OpeningCheckId,
    Guid LatestFailureCheckId,
    Guid? ResolutionCheckId,
    long FirstFailureSequence,
    long OpeningSequence,
    long LatestFailureSequence,
    long? ResolutionSequence,
    DateTimeOffset BeganAt,
    DateTimeOffset OpenedAt,
    DateTimeOffset LastObservedAt,
    DateTimeOffset? ResolvedAt,
    string OriginalReason,
    string LatestReason);

public sealed record IncidentHistoryPage(
    IReadOnlyList<IncidentHistoryItem> Items,
    long? NextBeforeOpeningSequence);

public sealed record HttpHistoryPruneResult(int IncidentsDeleted, int ChecksDeleted);

public interface IHttpMonitorHistoryStore
{
    Task<HttpCheckHistoryItem?> ReadCheckAsync(
        string projectKey,
        string monitorKey,
        Guid checkId,
        CancellationToken cancellationToken);

    Task<HttpCheckHistoryPage?> ListChecksAsync(
        string projectKey,
        string monitorKey,
        long? beforeSequence,
        int limit,
        CancellationToken cancellationToken);

    Task<IncidentHistoryItem?> ReadIncidentAsync(
        string projectKey,
        string monitorKey,
        Guid incidentId,
        CancellationToken cancellationToken);

    Task<IncidentHistoryPage?> ListIncidentsAsync(
        string projectKey,
        string monitorKey,
        long? beforeOpeningSequence,
        int limit,
        CancellationToken cancellationToken);

    Task<HttpHistoryPruneResult> PruneAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken);
}
