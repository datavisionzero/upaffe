using Microsoft.EntityFrameworkCore;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Notifications;
using Upaffe.Domain.Projects;

namespace Upaffe.Infrastructure.Persistence;

/// <summary>Adds intent rows in the same transaction as an incident transition.</summary>
internal static class NotificationIntentFactory
{
    public static Task OpenHttpAsync(UpaffeDbContext context, HttpMonitor monitor,
        Incident incident, DateTimeOffset now, CancellationToken cancellationToken) =>
        OpenAsync(context, monitor.ProjectId, monitor.Id, "http", monitor.Key,
            monitor.Name, incident.Id, incident.OriginalReason, incident.OpenedAt,
            now, cancellationToken);

    public static Task OpenPushAsync(UpaffeDbContext context, PushMonitor monitor,
        PushIncident incident, DateTimeOffset now, CancellationToken cancellationToken) =>
        OpenAsync(context, monitor.ProjectId, monitor.Id, "push", monitor.Key,
            monitor.Name, incident.Id, incident.OriginalReason, incident.OpenedAt,
            now, cancellationToken);

    public static Task ResolveHttpAsync(UpaffeDbContext context, HttpMonitor monitor,
        Incident incident, DateTimeOffset now, CancellationToken cancellationToken) =>
        ResolveAsync(context, monitor.ProjectId, monitor.Key, monitor.Name,
            "http", incident.Id, incident.OriginalReason,
            incident.ResolvedAt ?? now, now, cancellationToken);

    public static Task ResolvePushAsync(UpaffeDbContext context, PushMonitor monitor,
        PushIncident incident, DateTimeOffset now, CancellationToken cancellationToken) =>
        ResolveAsync(context, monitor.ProjectId, monitor.Key, monitor.Name,
            "push", incident.Id, incident.OriginalReason,
            incident.ResolvedAt ?? now, now, cancellationToken);

    private static async Task OpenAsync(UpaffeDbContext context, Guid projectId,
        Guid monitorId, string monitorType, string monitorKey, string monitorName,
        Guid incidentId, string reason, DateTimeOffset occurredAt, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var project = await context.Projects.SingleAsync(value => value.Id == projectId,
            cancellationToken);
        if (project.DeletedAt is not null || await SuppressedAsync(context,
            projectId, monitorId, monitorType, now, cancellationToken)) return;
        foreach (var recipient in project.Recipients)
            context.NotificationDeliveries.Add(NotificationDelivery.Queue(incidentId,
                NotificationKind.Alert, recipient, project.Key, project.Name, monitorKey,
                monitorName, monitorType, reason, occurredAt, now));
    }

    private static async Task ResolveAsync(UpaffeDbContext context, Guid projectId,
        string monitorKey, string monitorName, string monitorType, Guid incidentId,
        string reason, DateTimeOffset occurredAt, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var project = await context.Projects.SingleAsync(value => value.Id == projectId,
            cancellationToken);
        var alerts = await context.NotificationDeliveries.Where(value =>
            value.IncidentId == incidentId && value.Kind == NotificationKind.Alert)
            .ToListAsync(cancellationToken);
        foreach (var alert in alerts)
        {
            if (alert.State == DeliveryState.Accepted && project.DeletedAt is null
                && project.Recipients.Contains(alert.Recipient, StringComparer.OrdinalIgnoreCase))
                context.NotificationDeliveries.Add(NotificationDelivery.Queue(incidentId,
                    NotificationKind.Recovery, alert.Recipient, project.Key, project.Name,
                    monitorKey, monitorName, monitorType, reason, occurredAt, now));
            else alert.Obsolete(now);
        }
    }

    private static Task<bool> SuppressedAsync(UpaffeDbContext context, Guid projectId,
        Guid monitorId, string monitorType, DateTimeOffset now,
        CancellationToken cancellationToken) =>
        context.MaintenanceWindows.AnyAsync(value => value.EndedAt == null
            && value.EndsAt > now &&
            ((value.ScopeType == "project" && value.ScopeId == projectId)
             || (value.ScopeType == monitorType && value.ScopeId == monitorId)),
            cancellationToken);
}
