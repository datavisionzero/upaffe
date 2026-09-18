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
    public string OriginalReason { get; private set; } = string.Empty;
    public string LatestReason { get; private set; } = string.Empty;
    public bool IsOpen => ResolvedAt is null;

    public static PushIncident Open(PushReport failure, DateTimeOffset openedAt) => new(failure, openedAt);

    private static string StableReason(PushReport failure) =>
        failure.IsDeadlineObservation ? "report_missing" : "reported_failure";
}
