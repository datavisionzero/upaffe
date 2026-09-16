using Upaffe.Domain.Access;

namespace Upaffe.Application.Ports;

public sealed record OperatorLogin(
    Guid Id,
    string Email,
    string NormalizedEmail,
    string PasswordHash);

public sealed record AdmittedBrowser(
    Identity Identity,
    string Email,
    DateTimeOffset ExpiresAt);

/// <summary>The operator and revocable browser sessions as access acts need them.</summary>
public interface IBrowserSessionStore
{
    Task<OperatorLogin?> ReadOperatorAsync(CancellationToken cancellationToken);
    Task AddAsync(BrowserSession session, CancellationToken cancellationToken);
    Task<AdmittedBrowser?> AdmitAsync(
        byte[] secretHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task RevokeAsync(
        Guid sessionId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
