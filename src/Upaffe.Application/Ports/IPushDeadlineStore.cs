namespace Upaffe.Application.Ports;

public sealed record PushDeadlineLease(
    Guid MonitorId,
    Guid Token,
    long EvaluationGeneration,
    DateTimeOffset DeadlineAt);

public enum PushDeadlineCompletion
{
    Completed,
    Superseded,
    LeaseLost,
}

/// <summary>Claims and completes durable missing-report evaluation work.</summary>
public interface IPushDeadlineStore
{
    Task<PushDeadlineLease?> ClaimAsync(
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<PushDeadlineCompletion> CompleteAsync(
        PushDeadlineLease lease,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
