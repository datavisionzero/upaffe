namespace Upaffe.Domain.Monitoring;

/// <summary>One persistent record of a monitor's uninterrupted failure episode.</summary>
public sealed class Incident
{
    private Incident()
    {
    }

    private Incident(HttpCheck firstFailure, HttpCheck openingFailure, DateTimeOffset openedAt)
    {
        RequireFailure(firstFailure);
        RequireFailure(openingFailure);
        if (firstFailure.MonitorId != openingFailure.MonitorId
            || firstFailure.Sequence > openingFailure.Sequence
            || openedAt < openingFailure.CompletedAt)
        {
            throw new ArgumentException("An incident requires ordered failures from one monitor.", nameof(openingFailure));
        }

        Id = Guid.NewGuid();
        MonitorId = openingFailure.MonitorId;
        FirstFailureCheckId = firstFailure.Id;
        OpeningCheckId = openingFailure.Id;
        LatestFailureCheckId = openingFailure.Id;
        FirstFailureSequence = firstFailure.Sequence;
        OpeningSequence = openingFailure.Sequence;
        LatestFailureSequence = openingFailure.Sequence;
        BeganAt = firstFailure.CompletedAt ?? throw new ArgumentException("The first failure has no completion time.", nameof(firstFailure));
        OpenedAt = openedAt;
        LastObservedAt = openingFailure.CompletedAt ?? throw new ArgumentException("The opening failure has no completion time.", nameof(openingFailure));
        OriginalReason = firstFailure.FailureReason!;
        LatestReason = openingFailure.FailureReason!;
    }

    public Guid Id { get; private set; }
    public Guid MonitorId { get; private set; }
    public Guid FirstFailureCheckId { get; private set; }
    public Guid OpeningCheckId { get; private set; }
    public Guid LatestFailureCheckId { get; private set; }
    public Guid? ResolutionCheckId { get; private set; }
    public long FirstFailureSequence { get; private set; }
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

    public static Incident Open(HttpCheck firstFailure, HttpCheck openingFailure, DateTimeOffset openedAt) =>
        new(firstFailure, openingFailure, openedAt);

    public void ObserveFailure(HttpCheck failure)
    {
        EnsureOpen();
        RequireFailure(failure);
        if (failure.MonitorId != MonitorId || failure.Sequence <= LatestFailureSequence)
        {
            throw new ArgumentException("A newer failure from the same monitor is required.", nameof(failure));
        }

        LatestFailureCheckId = failure.Id;
        LatestFailureSequence = failure.Sequence;
        LastObservedAt = failure.CompletedAt ?? throw new ArgumentException("The failure has no completion time.", nameof(failure));
        LatestReason = failure.FailureReason!;
    }

    public void Resolve(HttpCheck success)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(success);
        if (!success.IsCompleted
            || success.Outcome != CheckOutcome.Success
            || success.MonitorId != MonitorId
            || success.Sequence <= LatestFailureSequence)
        {
            throw new ArgumentException("A fresh success from the same monitor is required.", nameof(success));
        }

        ResolutionCheckId = success.Id;
        ResolutionSequence = success.Sequence;
        ResolvedAt = success.CompletedAt;
    }

    private static void RequireFailure(HttpCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (!check.IsCompleted || check.Outcome != CheckOutcome.Failure)
        {
            throw new ArgumentException("A completed failed check is required.", nameof(check));
        }
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("A resolved incident cannot change.");
        }
    }
}
