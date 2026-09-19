using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Upaffe.Application.Ports;
using Upaffe.Domain.Notifications;

namespace Upaffe.Infrastructure.Persistence;

public sealed class EmailDeliveryStore(UpaffeDbContext context) : IEmailDeliveryStore
{
    public async Task<EmailDeliveryLease?> ClaimAsync(DateTimeOffset now,
        TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        if (leaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var id = await SelectIdAsync("""
            select id from notification_delivery
            where (state in ('Queued', 'Retrying') and next_attempt_at <= @now)
               or (state = 'Claimed' and lease_until <= @now)
            order by next_attempt_at, id
            for update skip locked
            limit 1
            """, "now", now, transaction, cancellationToken);
        if (id is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        var delivery = await context.NotificationDeliveries.SingleAsync(x => x.Id == id, cancellationToken);
        await context.Entry(delivery).ReloadAsync(cancellationToken);
        var token = Guid.NewGuid();
        var claimed = delivery.Claim(token, now, leaseDuration);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (!claimed) return null;
        return new(delivery.Id, token, delivery.IncidentId,
            delivery.Kind == NotificationKind.Alert ? "alert" : "recovery",
            delivery.Recipient, delivery.ProjectKey, delivery.ProjectName,
            delivery.MonitorKey, delivery.MonitorName, delivery.MonitorType,
            delivery.Reason, delivery.OccurredAt);
    }

    public async Task<bool> CompleteAsync(Guid deliveryId, Guid token,
        EmailSendResult result, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var id = await SelectIdAsync("""
            select id from notification_delivery where id = @id for update
            """, "id", deliveryId, transaction, cancellationToken);
        if (id is null) return false;
        var delivery = await context.NotificationDeliveries.SingleAsync(x => x.Id == id, cancellationToken);
        await context.Entry(delivery).ReloadAsync(cancellationToken);
        if (!delivery.IsClaimedBy(token))
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        delivery.Complete(token, result.AcceptedBySmtp, result.Transient,
            result.FailureCode, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<Guid?> SelectIdAsync(string sql, string name, object value,
        IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = sql;
        command.Parameters.Add(name == "id"
            ? new NpgsqlParameter<Guid>(name, (Guid)value)
            : new NpgsqlParameter<DateTimeOffset>(name, (DateTimeOffset)value));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id ? id : null;
    }
}
