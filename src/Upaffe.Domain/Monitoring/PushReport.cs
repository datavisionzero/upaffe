namespace Upaffe.Domain.Monitoring;

/// <summary>One immutable external or deadline-generated push observation.</summary>
public sealed class PushReport
{
    public const int MaximumDiagnosticReasonLength = 1_024;

    private PushReport()
    {
    }

    private PushReport(
        Guid monitorId,
        Guid reportId,
        long evaluationGeneration,
        long sequence,
        DateTimeOffset observedAt,
        DateTimeOffset receivedAt,
        ReportOutcome outcome,
        string? diagnosticReason,
        bool isDeadlineObservation)
    {
        if (monitorId == Guid.Empty || reportId == Guid.Empty)
        {
            throw new ArgumentException("A monitor and report identity are required.");
        }

        if (evaluationGeneration < 1
            || sequence < 1
            || observedAt > receivedAt.AddMinutes(5)
            || observedAt < receivedAt.AddDays(-90))
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        var reason = string.IsNullOrWhiteSpace(diagnosticReason) ? null : diagnosticReason.Trim();
        if (reason?.EnumerateRunes().Count() > MaximumDiagnosticReasonLength
            || (outcome == ReportOutcome.Success && reason is not null))
        {
            throw new ArgumentException("Only a failure may carry a diagnostic reason of at most 1024 characters.", nameof(diagnosticReason));
        }

        Id = Guid.NewGuid();
        MonitorId = monitorId;
        ReportId = reportId;
        EvaluationGeneration = evaluationGeneration;
        Sequence = sequence;
        ObservedAt = observedAt;
        ReceivedAt = receivedAt;
        Outcome = outcome;
        DiagnosticReason = reason;
        IsDeadlineObservation = isDeadlineObservation;
    }

    public Guid Id { get; private set; }
    public Guid MonitorId { get; private set; }
    public Guid ReportId { get; private set; }
    public long EvaluationGeneration { get; private set; }
    public long Sequence { get; private set; }
    public DateTimeOffset ObservedAt { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public ReportOutcome Outcome { get; private set; }
    public string? DiagnosticReason { get; private set; }
    public bool IsDeadlineObservation { get; private set; }

    public static PushReport Receive(
        Guid monitorId,
        Guid reportId,
        long evaluationGeneration,
        long sequence,
        DateTimeOffset observedAt,
        DateTimeOffset receivedAt,
        ReportOutcome outcome,
        string? diagnosticReason) => new(
            monitorId,
            reportId,
            evaluationGeneration,
            sequence,
            observedAt,
            receivedAt,
            outcome,
            diagnosticReason,
            false);

    public static PushReport Missing(
        Guid monitorId,
        long evaluationGeneration,
        long sequence,
        DateTimeOffset deadline,
        DateTimeOffset processedAt) => new(
            monitorId,
            Guid.NewGuid(),
            evaluationGeneration,
            sequence,
            deadline,
            processedAt,
            ReportOutcome.Failure,
            "report_missing",
            true);
}
