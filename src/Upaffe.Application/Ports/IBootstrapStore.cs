using Upaffe.Domain.Access;

namespace Upaffe.Application.Ports;

public sealed record BootstrapState(bool Required);

/// <summary>The transactional boundary for local operator setup and recovery.</summary>
public interface IBootstrapStore
{
    Task<BootstrapState> ReadAsync(CancellationToken cancellationToken);
    Task<IssuedCredential?> EstablishAsync(
        Operator @operator, string credentialName, DateTimeOffset now, CancellationToken cancellationToken);
    Task<IssuedCredential?> RecoverCredentialAsync(
        string name, DateTimeOffset now, CancellationToken cancellationToken);
}
