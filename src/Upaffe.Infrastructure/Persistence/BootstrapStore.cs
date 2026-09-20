using System.Data;
using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;

namespace Upaffe.Infrastructure.Persistence;

/// <summary>Serializes local operator setup and keeps credential issuance atomic.</summary>
public sealed class BootstrapStore(UpaffeDbContext context) : IBootstrapStore
{
    private const long AdvisoryLockKey = 0x_0B_0057;

    public async Task<BootstrapState> ReadAsync(CancellationToken cancellationToken) =>
        new(!await context.Operators.AnyAsync(cancellationToken));

    public async Task<IssuedCredential?> EstablishAsync(
        Operator @operator, string credentialName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginLockedAsync(cancellationToken);
        if (await context.Operators.AnyAsync(cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var credential = ManagementCredential.Create(@operator.Id, credentialName, now);
        var issued = ManagementCredentialSecret.Issue(credential.Id, now);
        context.Operators.Add(@operator);
        context.ManagementCredentials.Add(credential);
        context.ManagementCredentialSecrets.Add(issued.Secret);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new IssuedCredential(
            new CredentialMetadata(credential.Id, credential.Name, credential.CreatedAt, null, null),
            ManagementToken.Format(credential.Id, issued.Value));
    }

    public async Task<IssuedCredential?> RecoverCredentialAsync(
        string name, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await BeginLockedAsync(cancellationToken);
        var credential = await context.ManagementCredentials
            .FromSqlInterpolated($"select * from management_credential where name = {name} for update")
            .SingleOrDefaultAsync(cancellationToken);
        if (credential is null || credential.RevokedAt is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        // Recovery invalidates every prior token immediately, including a rotating overlap.
        var previous = await context.ManagementCredentialSecrets
            .Where(value => value.CredentialId == credential.Id)
            .ToListAsync(cancellationToken);
        context.ManagementCredentialSecrets.RemoveRange(previous);
        await context.SaveChangesAsync(cancellationToken);

        credential.RecordRotation(now);
        var issued = ManagementCredentialSecret.Issue(credential.Id, now);
        context.ManagementCredentialSecrets.Add(issued.Secret);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new IssuedCredential(
            new CredentialMetadata(credential.Id, credential.Name, credential.CreatedAt,
                credential.RotatedAt, credential.RevokedAt),
            ManagementToken.Format(credential.Id, issued.Value));
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginLockedAsync(
        CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "select pg_advisory_xact_lock({0})", [AdvisoryLockKey], cancellationToken);
        return transaction;
    }
}
