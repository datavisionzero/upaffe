using Upaffe.Domain.Monitoring;

namespace Upaffe.Application.Ports;

public sealed record PushReportSubmission(
    Guid ReportId,
    DateTimeOffset ObservedAt,
    ReportOutcome Outcome,
    string? DiagnosticReason);

public sealed record PushReportReceipt(
    Guid ReportId,
    DateTimeOffset ReceivedAt,
    long Sequence,
    bool Applied,
    bool Duplicate);

public enum PushReportMutation
{
    Accepted,
    Rejected,
    Conflict,
    Paused,
}

public sealed record PushReportMutationResult(
    PushReportMutation Outcome,
    PushReportReceipt? Receipt = null);

public interface IPushReportStore
{
    Task<PushReportMutationResult> SubmitAsync(
        string token,
        PushReportSubmission submission,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken);

    Task<PushReportMutationResult> SubmitSimpleSuccessAsync(
        string token,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken);
}
