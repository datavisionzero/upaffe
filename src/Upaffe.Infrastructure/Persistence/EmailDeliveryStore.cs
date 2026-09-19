using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Upaffe.Application.Ports;
using Upaffe.Domain.Notifications;
using Upaffe.Domain.Monitoring;

namespace Upaffe.Infrastructure.Persistence;

public sealed class EmailDeliveryStore(UpaffeDbContext context) : IEmailDeliveryStore
{
    public async Task<bool> ReconcileAsync(DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var httpId = await SelectIdAsync("""
            select i.id from incident i
            join http_monitor m on m.id = i.monitor_id
            join project p on p.id = m.project_id
            where i.resolved_at is null and i.notification_decision_at is null
              and m.deleted_at is null and m.state <> 'Paused'
              and p.deleted_at is null
              and not exists (
                select 1 from maintenance_window w
                where w.ended_at is null and w.ends_at > @now
                  and ((w.scope_type = 'project' and w.scope_id = p.id)
                    or (w.scope_type = 'http' and w.scope_id = m.id)))
            order by i.opened_at, i.id
            for update of m skip locked
            limit 1
            """, "now", now, transaction, cancellationToken);
        if (httpId is not null)
        {
            var incident = await context.Incidents.SingleAsync(value => value.Id == httpId,
                cancellationToken);
            var monitor = await context.HttpMonitors.SingleAsync(value =>
                value.Id == incident.MonitorId, cancellationToken);
            await NotificationIntentFactory.OpenHttpAsync(context, monitor,
                incident, now, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        var pushId = await SelectIdAsync("""
            select i.id from push_incident i
            join push_monitor m on m.id = i.monitor_id
            join project p on p.id = m.project_id
            where i.resolved_at is null and i.notification_decision_at is null
              and m.deleted_at is null and m.state <> 'Paused'
              and p.deleted_at is null
              and not exists (
                select 1 from maintenance_window w
                where w.ended_at is null and w.ends_at > @now
                  and ((w.scope_type = 'project' and w.scope_id = p.id)
                    or (w.scope_type = 'push' and w.scope_id = m.id)))
            order by i.opened_at, i.id
            for update of m skip locked
            limit 1
            """, "now", now, transaction, cancellationToken);
        if (pushId is not null)
        {
            var incident = await context.PushIncidents.SingleAsync(value => value.Id == pushId,
                cancellationToken);
            var monitor = await context.PushMonitors.SingleAsync(value =>
                value.Id == incident.MonitorId, cancellationToken);
            await NotificationIntentFactory.OpenPushAsync(context, monitor,
                incident, now, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        var acceptedId = await SelectIdAsync("""
            select d.id from notification_delivery d
            where d.kind = 'Alert' and d.state = 'Accepted'
              and d.recovery_decision_at is null
              and ((d.monitor_type = 'http' and exists (
                     select 1 from incident i where i.id = d.incident_id
                       and i.resolved_at is not null))
                or (d.monitor_type = 'push' and exists (
                     select 1 from push_incident i where i.id = d.incident_id
                       and i.resolved_at is not null)))
            order by d.accepted_at, d.id
            for update of d skip locked
            limit 1
            """, "now", now, transaction, cancellationToken);
        if (acceptedId is not null)
        {
            var alert = await context.NotificationDeliveries.SingleAsync(value =>
                value.Id == acceptedId, cancellationToken);
            await ReconcileRecoveryAsync(alert, now, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        await transaction.CommitAsync(cancellationToken);
        return false;
    }

    private async Task ReconcileRecoveryAsync(NotificationDelivery alert,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        Guid projectId;
        bool removed;
        DateTimeOffset? resolvedAt;
        if (alert.MonitorType == "http")
        {
            var incident = await context.Incidents.AsNoTracking().SingleAsync(value =>
                value.Id == alert.IncidentId, cancellationToken);
            var monitor = await context.HttpMonitors.AsNoTracking().SingleAsync(value =>
                value.Id == incident.MonitorId, cancellationToken);
            projectId = monitor.ProjectId;
            removed = monitor.DeletedAt is not null;
            resolvedAt = incident.ResolvedAt;
        }
        else
        {
            var incident = await context.PushIncidents.AsNoTracking().SingleAsync(value =>
                value.Id == alert.IncidentId, cancellationToken);
            var monitor = await context.PushMonitors.AsNoTracking().SingleAsync(value =>
                value.Id == incident.MonitorId, cancellationToken);
            projectId = monitor.ProjectId;
            removed = monitor.DeletedAt is not null;
            resolvedAt = incident.ResolvedAt;
        }
        var project = await context.Projects.AsNoTracking().SingleAsync(value =>
            value.Id == projectId, cancellationToken);
        if (!removed && project.DeletedAt is null
            && project.Recipients.Contains(alert.Recipient, StringComparer.OrdinalIgnoreCase)
            && !await context.NotificationDeliveries.AnyAsync(value =>
                value.IncidentId == alert.IncidentId && value.Kind == NotificationKind.Recovery
                && value.RecipientKey == alert.RecipientKey, cancellationToken))
            context.NotificationDeliveries.Add(NotificationDelivery.Queue(alert.IncidentId,
                NotificationKind.Recovery, alert.Recipient, project.Key, project.Name,
                alert.MonitorKey, alert.MonitorName, alert.MonitorType, alert.Reason,
                resolvedAt ?? now, now));
        alert.MarkRecoveryDecision(now);
    }

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
