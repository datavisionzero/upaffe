using Upaffe.Domain.Monitoring;

namespace Upaffe.Application.Ports;

public sealed record PushReportHistoryItem(
    Guid Id,
    Guid ReportId,
    long EvaluationGeneration,
    long Sequence,
    DateTimeOffset ObservedAt,
    DateTimeOffset ReceivedAt,
    ReportOutcome Outcome,
    string? Reason,
    bool Applicable);

public sealed record PushReportHistoryPage(
    IReadOnlyList<PushReportHistoryItem> Items,
    long? NextBeforeSequence);

public sealed record PushIncidentHistoryItem(
    Guid Id,
    Guid OpeningReportId,
    Guid LatestFailureReportId,
    Guid? ResolutionReportId,
    long OpeningSequence,
    long LatestFailureSequence,
    long? ResolutionSequence,
    DateTimeOffset BeganAt,
    DateTimeOffset OpenedAt,
    DateTimeOffset LastObservedAt,
    DateTimeOffset? ResolvedAt,
    string OriginalReason,
    string LatestReason);

public sealed record PushIncidentHistoryPage(
    IReadOnlyList<PushIncidentHistoryItem> Items,
    long? NextBeforeOpeningSequence);

public sealed record PushHistoryPruneResult(int IncidentsDeleted, int ReportsDeleted);

public interface IPushMonitorHistoryStore
{
    Task<PushReportHistoryItem?> ReadReportAsync(
        string projectKey,
        string monitorKey,
        Guid reportId,
        CancellationToken cancellationToken);

    Task<PushReportHistoryPage?> ListReportsAsync(
        string projectKey,
        string monitorKey,
        long? beforeSequence,
        int limit,
        CancellationToken cancellationToken);

    Task<PushIncidentHistoryItem?> ReadIncidentAsync(
        string projectKey,
        string monitorKey,
        Guid incidentId,
        CancellationToken cancellationToken);

    Task<PushIncidentHistoryPage?> ListIncidentsAsync(
        string projectKey,
        string monitorKey,
        long? beforeOpeningSequence,
        int limit,
        CancellationToken cancellationToken);

    Task<PushHistoryPruneResult> PruneAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken);
}
