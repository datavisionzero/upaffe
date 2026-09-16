using System.Data;
using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;

namespace Upaffe.Infrastructure.Persistence;

/// <summary>Serializes the one transition from a grant to the singleton operator.</summary>
public sealed class BootstrapStore(UpaffeDbContext context) : IBootstrapStore
{
    private const long AdvisoryLockKey = 0x_0B_0057;

    public async Task ArmAsync(
        byte[]? secretHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginLockedAsync(cancellationToken);
        if (await context.Operators.AnyAsync(cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var grant = await context.BootstrapGrants.SingleOrDefaultAsync(cancellationToken);
        if (secretHash is null)
        {
            if (grant is not null)
            {
                context.BootstrapGrants.Remove(grant);
                await context.SaveChangesAsync(cancellationToken);
            }
        }
        else if (grant is null)
        {
            context.BootstrapGrants.Add(BootstrapGrant.Arm(secretHash, now));
            await context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            grant.Rearm(secretHash, now);
            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<BootstrapState> ReadAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await context.Operators.AnyAsync(cancellationToken))
        {
            return new(false, false);
        }

        var grant = await context.BootstrapGrants.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return new(true, grant is not null && grant.ConsumedAt is null && now < grant.ExpiresAt);
    }

    public async Task<BootstrapProof> CheckAsync(
        byte[] secretHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await context.Operators.AnyAsync(cancellationToken))
        {
            return BootstrapProof.Closed;
        }

        var grant = await context.BootstrapGrants.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return grant?.Accepts(secretHash, now) == true
            ? BootstrapProof.Accepted
            : BootstrapProof.Rejected;
    }

    public async Task<BootstrapProof> EstablishAsync(
        byte[] secretHash,
        Operator @operator,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginLockedAsync(cancellationToken);
        if (await context.Operators.AnyAsync(cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return BootstrapProof.Closed;
        }

        var grant = await context.BootstrapGrants.SingleOrDefaultAsync(cancellationToken);
        if (grant?.Accepts(secretHash, now) != true)
        {
            await transaction.CommitAsync(cancellationToken);
            return BootstrapProof.Rejected;
        }

        grant.Consume(now);
        context.Operators.Add(@operator);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return BootstrapProof.Accepted;
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginLockedAsync(
        CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "select pg_advisory_xact_lock({0})",
            [AdvisoryLockKey],
            cancellationToken);
        return transaction;
    }
}
