namespace Upaffe.Domain.Monitoring;

/// <summary>One persistent push monitor failure episode.</summary>
public sealed class PushIncident
{
    private PushIncident()
    {
    }

    private PushIncident(PushReport failure, DateTimeOffset openedAt)
    {
        if (failure.Outcome != ReportOutcome.Failure || openedAt < failure.ReceivedAt)
        {
            throw new ArgumentException("A received failure is required.", nameof(failure));
        }

        Id = Guid.NewGuid();
        MonitorId = failure.MonitorId;
        OpeningReportId = failure.Id;
        LatestFailureReportId = failure.Id;
        OpeningSequence = failure.Sequence;
        LatestFailureSequence = failure.Sequence;
        BeganAt = failure.ObservedAt;
        OpenedAt = openedAt;
        LastObservedAt = failure.ObservedAt;
        OriginalReason = StableReason(failure);
        LatestReason = OriginalReason;
    }

    public Guid Id { get; private set; }
    public Guid MonitorId { get; private set; }
    public Guid OpeningReportId { get; private set; }
    public Guid LatestFailureReportId { get; private set; }
    public Guid? ResolutionReportId { get; private set; }
    public long OpeningSequence { get; private set; }
    public long LatestFailureSequence { get; private set; }
    public long? ResolutionSequence { get; private set; }
    public DateTimeOffset BeganAt { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset LastObservedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public DateTimeOffset? NotificationDecisionAt { get; private set; }
    public string OriginalReason { get; private set; } = string.Empty;
    public string LatestReason { get; private set; } = string.Empty;
    public bool IsOpen => ResolvedAt is null;

    public void MarkNotificationDecision(DateTimeOffset now) =>
        NotificationDecisionAt ??= now;

    public static PushIncident Open(PushReport failure, DateTimeOffset openedAt) => new(failure, openedAt);

    public void ObserveFailure(PushReport failure)
    {
        EnsureOpen();
        if (!failure.Applicable
            || failure.Outcome != ReportOutcome.Failure
            || failure.MonitorId != MonitorId
            || failure.Sequence <= LatestFailureSequence)
        {
            throw new ArgumentException("A newer applicable failure from the same monitor is required.", nameof(failure));
        }

        LatestFailureReportId = failure.Id;
        LatestFailureSequence = failure.Sequence;
        LastObservedAt = failure.ObservedAt;
        LatestReason = StableReason(failure);
    }

    public void Resolve(PushReport success)
    {
        EnsureOpen();
        if (!success.Applicable
            || success.Outcome != ReportOutcome.Success
            || success.MonitorId != MonitorId
            || success.Sequence <= LatestFailureSequence)
        {
            throw new ArgumentException("A fresh applicable success from the same monitor is required.", nameof(success));
        }

        ResolutionReportId = success.Id;
        ResolutionSequence = success.Sequence;
        ResolvedAt = success.ReceivedAt;
    }

    private static string StableReason(PushReport failure) =>
        failure.IsDeadlineObservation ? "report_missing" : "reported_failure";

    private void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("A resolved incident cannot change.");
        }
    }
}
