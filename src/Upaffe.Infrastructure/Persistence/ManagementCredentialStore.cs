using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;

namespace Upaffe.Infrastructure.Persistence;

/// <summary>Creates, rotates, revokes, and admits hashed management credentials.</summary>
public sealed class ManagementCredentialStore(UpaffeDbContext context) : IManagementCredentialStore
{
    public async Task<CredentialMutationResult> CreateAsync(
        Guid operatorId,
        string name,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var credential = ManagementCredential.Create(operatorId, name, now);
        var issued = ManagementCredentialSecret.Issue(credential.Id, now);
        context.ManagementCredentials.Add(credential);
        context.ManagementCredentialSecrets.Add(issued.Secret);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsNameConflict(exception))
        {
            return new(CredentialMutation.Conflict);
        }

        return new(
            CredentialMutation.Changed,
            new IssuedCredential(
                Metadata(credential),
                ManagementToken.Format(credential.Id, issued.Value)));
    }

    public async Task<IReadOnlyList<CredentialMetadata>> ListAsync(
        Guid operatorId,
        CancellationToken cancellationToken) =>
        await context.ManagementCredentials
            .AsNoTracking()
            .Where(value => value.OperatorId == operatorId)
            .OrderBy(value => value.Name)
            .ThenBy(value => value.Id)
            .Select(value => new CredentialMetadata(
                value.Id,
                value.Name,
                value.CreatedAt,
                value.RotatedAt,
                value.RevokedAt))
            .ToListAsync(cancellationToken);

    public async Task<CredentialMutationResult> RotateAsync(
        Guid credentialId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var credential = await LockedAsync(credentialId, operatorId, cancellationToken);
        if (credential is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(CredentialMutation.Missing);
        }

        if (credential.RevokedAt is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(CredentialMutation.Revoked);
        }

        var current = await context.ManagementCredentialSecrets.SingleAsync(
            value => value.CredentialId == credential.Id && value.ExpiresAt == null,
            cancellationToken);
        var overlapEnd = now.Add(ManagementCredentialSecret.RotationOverlap);
        current.ExpireAt(overlapEnd);
        credential.RecordRotation(now);
        var issued = ManagementCredentialSecret.Issue(credential.Id, now);
        context.ManagementCredentialSecrets.Add(issued.Secret);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new(
            CredentialMutation.Changed,
            new IssuedCredential(
                Metadata(credential),
                ManagementToken.Format(credential.Id, issued.Value),
                overlapEnd));
    }

    public async Task<CredentialMutation> RevokeAsync(
        Guid credentialId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var credential = await LockedAsync(credentialId, operatorId, cancellationToken);
        if (credential is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return CredentialMutation.Missing;
        }

        credential.Revoke(now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return CredentialMutation.Changed;
    }

    public async Task<AdmittedManagement?> AdmitAsync(
        Guid credentialId,
        byte[] secretHash,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await (
                from credential in context.ManagementCredentials.AsNoTracking()
                join secret in context.ManagementCredentialSecrets.AsNoTracking()
                    on credential.Id equals secret.CredentialId
                where credential.Id == credentialId
                    && credential.RevokedAt == null
                    && secret.SecretHash == secretHash
                    && (secret.ExpiresAt == null || now < secret.ExpiresAt)
                select new AdmittedManagement(
                    new Identity(
                        credential.OperatorId,
                        AccessPath.ManagementCredential,
                        credential.Id),
                    credential.Name))
            .SingleOrDefaultAsync(cancellationToken);

    private Task<ManagementCredential?> LockedAsync(
        Guid credentialId,
        Guid operatorId,
        CancellationToken cancellationToken) =>
        context.ManagementCredentials
            .FromSqlInterpolated(
                $"select * from management_credential where id = {credentialId} and operator_id = {operatorId} for update")
            .SingleOrDefaultAsync(cancellationToken);

    private static CredentialMetadata Metadata(ManagementCredential credential) => new(
        credential.Id,
        credential.Name,
        credential.CreatedAt,
        credential.RotatedAt,
        credential.RevokedAt);

    private static bool IsNameConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "management_credential_operator_name",
        };
}
