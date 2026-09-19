using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Upaffe.Application.Ports;
using Upaffe.Domain.Notifications;
using Upaffe.Domain.Monitoring;

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

    public async Task<EmailPreparation> PrepareAsync(Guid deliveryId, Guid token,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var id = await SelectIdAsync("""
            select id from notification_delivery where id = @id for update
            """, "id", deliveryId, transaction, cancellationToken);
        if (id is null) return EmailPreparation.LeaseLost;
        var delivery = await context.NotificationDeliveries.SingleAsync(x => x.Id == id, cancellationToken);
        await context.Entry(delivery).ReloadAsync(cancellationToken);
        if (!delivery.IsClaimedBy(token)) return EmailPreparation.LeaseLost;
        var state = await EligibilityAsync(delivery, now, cancellationToken);
        if (state == EmailPreparation.Obsolete) delivery.Obsolete(now);
        else if (state == EmailPreparation.Deferred) delivery.Defer(token, now);
        else delivery.BeginAttempt(token, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return state;
    }

    private async Task<EmailPreparation> EligibilityAsync(NotificationDelivery delivery,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        Guid projectId;
        Guid monitorId;
        bool paused;
        bool deleted;
        bool incidentOpen;
        if (delivery.MonitorType == "http")
        {
            var incident = await context.Incidents.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == delivery.IncidentId, cancellationToken);
            if (incident is null) return EmailPreparation.Obsolete;
            var monitor = await context.HttpMonitors.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == incident.MonitorId, cancellationToken);
            if (monitor is null) return EmailPreparation.Obsolete;
            projectId = monitor.ProjectId;
            monitorId = monitor.Id;
            paused = monitor.State == MonitorState.Paused;
            deleted = monitor.DeletedAt is not null;
            incidentOpen = incident.IsOpen;
        }
        else
        {
            var incident = await context.PushIncidents.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == delivery.IncidentId, cancellationToken);
            if (incident is null) return EmailPreparation.Obsolete;
            var monitor = await context.PushMonitors.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == incident.MonitorId, cancellationToken);
            if (monitor is null) return EmailPreparation.Obsolete;
            projectId = monitor.ProjectId;
            monitorId = monitor.Id;
            paused = monitor.State == MonitorState.Paused;
            deleted = monitor.DeletedAt is not null;
            incidentOpen = incident.IsOpen;
        }
        var project = await context.Projects.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == projectId, cancellationToken);
        if (project is null || project.DeletedAt is not null || deleted
            || !project.Recipients.Contains(delivery.Recipient, StringComparer.OrdinalIgnoreCase))
            return EmailPreparation.Obsolete;
        if (delivery.Kind == NotificationKind.Alert && !incidentOpen)
            return EmailPreparation.Obsolete;
        if (delivery.Kind == NotificationKind.Recovery && incidentOpen)
            return EmailPreparation.Obsolete;
        if (paused) return EmailPreparation.Deferred;
        var maintenance = await context.MaintenanceWindows.AsNoTracking().AnyAsync(value =>
            value.EndedAt == null && value.EndsAt > now &&
            ((value.ScopeType == "project" && value.ScopeId == projectId) ||
             (value.ScopeType == delivery.MonitorType && value.ScopeId == monitorId)),
            cancellationToken);
        return maintenance ? EmailPreparation.Deferred : EmailPreparation.Send;
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
