using Upaffe.Domain.Access;

namespace Upaffe.Application.Ports;

public sealed record BootstrapState(bool Required, bool Available);

public enum BootstrapProof
{
    Accepted,
    Rejected,
    Closed,
}

/// <summary>The transactional boundary around the one-time bootstrap.</summary>
public interface IBootstrapStore
{
    Task ArmAsync(byte[]? secretHash, DateTimeOffset now, CancellationToken cancellationToken);
    Task<BootstrapState> ReadAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<BootstrapProof> CheckAsync(byte[] secretHash, DateTimeOffset now, CancellationToken cancellationToken);
    Task<BootstrapProof> EstablishAsync(
        byte[] secretHash,
        Operator @operator,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
