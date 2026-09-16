using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;

namespace Upaffe.Infrastructure.Persistence;

/// <summary>Persists and admits the operator's revocable browser sessions.</summary>
public sealed class BrowserSessionStore(UpaffeDbContext context) : IBrowserSessionStore
{
    public async Task<OperatorLogin?> ReadOperatorAsync(CancellationToken cancellationToken) =>
        await context.Operators
            .AsNoTracking()
            .Select(value => new OperatorLogin(
                value.Id,
                value.Email,
                value.NormalizedEmail,
                value.PasswordHash))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task AddAsync(BrowserSession session, CancellationToken cancellationToken)
    {
        context.BrowserSessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<AdmittedBrowser?> AdmitAsync(
        byte[] secretHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var row = await (
                from session in context.BrowserSessions
                join @operator in context.Operators on session.OperatorId equals @operator.Id
                where session.SecretHash == secretHash
                select new { Session = session, Operator = @operator })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null || !row.Session.IsValid(now))
        {
            return null;
        }

        if (row.Session.Touch(now))
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return new(
            new Identity(row.Operator.Id, AccessPath.BrowserSession, row.Session.Id),
            row.Operator.Email,
            row.Session.ExpiresAt);
    }

    public Task RevokeAsync(
        Guid sessionId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        context.BrowserSessions
            .Where(value => value.Id == sessionId
                && value.OperatorId == operatorId
                && value.RevokedAt == null)
            .ExecuteUpdateAsync(
                update => update.SetProperty(value => value.RevokedAt, now),
                cancellationToken);
}
