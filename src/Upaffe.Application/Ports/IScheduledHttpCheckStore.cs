namespace Upaffe.Application.Ports;

public sealed record ScheduledHttpCheckLease(
    Guid CheckId,
    Guid Token,
    HttpExecutionRequest Request);

public enum ScheduledHttpCheckCompletion
{
    Completed,
    AlreadyCompleted,
    LeaseLost,
}

/// <summary>Claims and completes durable scheduled HTTP work around the network boundary.</summary>
public interface IScheduledHttpCheckStore
{
    Task<ScheduledHttpCheckLease?> ClaimAsync(
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<ScheduledHttpCheckCompletion> CompleteAsync(
        Guid checkId,
        Guid leaseToken,
        HttpExecutionResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
