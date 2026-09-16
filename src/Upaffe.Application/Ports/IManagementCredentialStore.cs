using Upaffe.Domain.Access;

namespace Upaffe.Application.Ports;

public sealed record CredentialMetadata(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RotatedAt,
    DateTimeOffset? RevokedAt);

public sealed record IssuedCredential(
    CredentialMetadata Credential,
    string Token,
    DateTimeOffset? PreviousValidUntil = null);

public sealed record AdmittedManagement(Identity Identity, string CredentialName);

public enum CredentialMutation
{
    Changed,
    Missing,
    Conflict,
    Revoked,
}

public sealed record CredentialMutationResult(
    CredentialMutation Outcome,
    IssuedCredential? Issued = null);

/// <summary>The transactional boundary for named automation credentials.</summary>
public interface IManagementCredentialStore
{
    Task<CredentialMutationResult> CreateAsync(
        Guid operatorId,
        string name,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<CredentialMetadata>> ListAsync(
        Guid operatorId,
        CancellationToken cancellationToken);
    Task<CredentialMutationResult> RotateAsync(
        Guid credentialId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<CredentialMutation> RevokeAsync(
        Guid credentialId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<AdmittedManagement?> AdmitAsync(
        Guid credentialId,
        byte[] secretHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
