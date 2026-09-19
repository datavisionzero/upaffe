using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Notifications;

namespace Upaffe.Infrastructure.Persistence;

public sealed class EmailStatusStore(UpaffeDbContext context) : IEmailStatusStore
{
    public Task<bool> ProjectExistsAsync(string projectKey, CancellationToken cancellationToken) =>
        context.Projects.AsNoTracking().AnyAsync(value => value.Key == projectKey
            && value.DeletedAt == null, cancellationToken);

    public async Task<EmailDeliveryPage> ListAsync(string? projectKey,
        string? monitorType, string? monitorKey, Guid? incidentId, string? state,
        int limit, int offset, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var query = Query(projectKey);
        if (monitorType is not null) query = query.Where(value => value.MonitorType == monitorType);
        if (monitorKey is not null) query = query.Where(value => value.MonitorKey == monitorKey);
        if (incidentId is not null) query = query.Where(value => value.IncidentId == incidentId);
        if (state is not null) query = query.Where(value => value.State == ParseState(state));
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(value => value.CreatedAt)
            .ThenByDescending(value => value.Id).Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);
        var items = new List<EmailDeliveryStatus>(rows.Count);
        foreach (var row in rows)
            items.Add(await StatusAsync(row, now, cancellationToken));
        return new(items, total, limit, offset, offset + rows.Count < total);
    }

    public async Task<EmailDeliverySummary> SummaryAsync(string? projectKey,
        CancellationToken cancellationToken)
    {
        var query = Query(projectKey);
        var pending = query.Where(value => value.State == DeliveryState.Queued
            || value.State == DeliveryState.Claimed || value.State == DeliveryState.Retrying);
        return new(projectKey,
            await pending.CountAsync(cancellationToken),
            await query.CountAsync(value => value.State == DeliveryState.Retrying, cancellationToken),
            await query.CountAsync(value => value.State == DeliveryState.TerminalFailure, cancellationToken),
            await query.CountAsync(value => value.State == DeliveryState.Accepted, cancellationToken),
            await pending.MinAsync(value => (DateTimeOffset?)value.CreatedAt, cancellationToken));
    }

    public async Task<IncidentEmailStatus?> ReadIncidentAsync(Guid incidentId,
        string monitorType, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Guid projectId;
        Guid monitorId;
        string monitorKey;
        bool open;
        DateTimeOffset? decidedAt;
        bool paused;
        if (monitorType == "http")
        {
            var incident = await context.Incidents.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == incidentId, cancellationToken);
            if (incident is null) return null;
            var monitor = await context.HttpMonitors.AsNoTracking().SingleAsync(
                value => value.Id == incident.MonitorId, cancellationToken);
            projectId = monitor.ProjectId;
            monitorId = monitor.Id;
            monitorKey = monitor.Key;
            open = incident.IsOpen;
            decidedAt = incident.NotificationDecisionAt;
            paused = monitor.State == MonitorState.Paused;
        }
        else
        {
            var incident = await context.PushIncidents.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == incidentId, cancellationToken);
            if (incident is null) return null;
            var monitor = await context.PushMonitors.AsNoTracking().SingleAsync(
                value => value.Id == incident.MonitorId, cancellationToken);
            projectId = monitor.ProjectId;
            monitorId = monitor.Id;
            monitorKey = monitor.Key;
            open = incident.IsOpen;
            decidedAt = incident.NotificationDecisionAt;
            paused = monitor.State == MonitorState.Paused;
        }
        var project = await context.Projects.AsNoTracking().SingleAsync(value =>
            value.Id == projectId, cancellationToken);
        var rows = await context.NotificationDeliveries.AsNoTracking()
            .Where(value => value.IncidentId == incidentId)
            .OrderBy(value => value.Kind).ThenBy(value => value.Recipient)
            .ToListAsync(cancellationToken);
        var items = new List<EmailDeliveryStatus>(rows.Count);
        foreach (var row in rows) items.Add(await StatusAsync(row, now, cancellationToken));
        var reason = paused ? "paused" : await MaintenanceActiveAsync(projectId,
            monitorId, monitorType, now, cancellationToken) ? "maintenance" : null;
        var announcement = items.Any(value => value.Kind == "alert" && value.State == "accepted")
            ? "announced"
            : items.Any(value => value.Kind == "alert" && value.State == "terminal_failure")
                ? "failed"
            : items.Any(value => value.Kind == "alert" && value.State == "suppressed")
                ? "suppressed"
            : items.Any(value => value.Kind == "alert" && value.State is "queued" or "claimed" or "retrying")
                ? "pending"
            : open && decidedAt is null
                ? reason is null ? "awaiting_reconciliation" : "suppressed"
            : open ? "no_recipients" : "silent";
        return new(incidentId, project.Key, monitorType, monitorKey, open,
            announcement, reason, items);
    }

    private IQueryable<NotificationDelivery> Query(string? projectKey)
    {
        var query = context.NotificationDeliveries.AsNoTracking();
        return projectKey is null ? query : query.Where(value => value.ProjectKey == projectKey);
    }

    private async Task<EmailDeliveryStatus> StatusAsync(NotificationDelivery row,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var reason = await SuppressionAsync(row, now, cancellationToken);
        var state = reason is not null ? "suppressed" : State(row.State);
        return new(row.Id, row.IncidentId,
            row.Kind == NotificationKind.Alert ? "alert" : "recovery",
            row.Recipient, row.ProjectKey, row.MonitorType, row.MonitorKey,
            state, reason, row.AttemptCount, row.LastAttemptAt,
            row.State is DeliveryState.Queued or DeliveryState.Retrying ? row.NextAttemptAt : null,
            row.AcceptedAt, row.TerminalAt, row.LastErrorCode, row.CreatedAt);
    }

    private async Task<string?> SuppressionAsync(NotificationDelivery row,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (row.State is not (DeliveryState.Queued or DeliveryState.Retrying or DeliveryState.Claimed)
            || row.ActiveAttemptToken is not null) return null;
        var project = await context.Projects.AsNoTracking().SingleOrDefaultAsync(value =>
            value.Key == row.ProjectKey, cancellationToken);
        if (project is null || project.DeletedAt is not null) return "removed";
        Guid monitorId;
        bool paused;
        if (row.MonitorType == "http")
        {
            var monitor = await context.HttpMonitors.AsNoTracking().SingleOrDefaultAsync(value =>
                value.ProjectId == project.Id && value.Key == row.MonitorKey, cancellationToken);
            if (monitor is null || monitor.DeletedAt is not null) return "removed";
            monitorId = monitor.Id;
            paused = monitor.State == MonitorState.Paused;
        }
        else
        {
            var monitor = await context.PushMonitors.AsNoTracking().SingleOrDefaultAsync(value =>
                value.ProjectId == project.Id && value.Key == row.MonitorKey, cancellationToken);
            if (monitor is null || monitor.DeletedAt is not null) return "removed";
            monitorId = monitor.Id;
            paused = monitor.State == MonitorState.Paused;
        }
        if (paused) return "paused";
        return await MaintenanceActiveAsync(project.Id, monitorId, row.MonitorType,
            now, cancellationToken) ? "maintenance" : null;
    }

    private Task<bool> MaintenanceActiveAsync(Guid projectId, Guid monitorId,
        string monitorType, DateTimeOffset now, CancellationToken cancellationToken) =>
        context.MaintenanceWindows.AsNoTracking().AnyAsync(value =>
            value.EndedAt == null && value.EndsAt > now &&
            ((value.ScopeType == "project" && value.ScopeId == projectId)
            || (value.ScopeType == monitorType && value.ScopeId == monitorId)),
            cancellationToken);

    private static string State(DeliveryState value) => value switch
    {
        DeliveryState.Queued => "queued",
        DeliveryState.Claimed => "claimed",
        DeliveryState.Retrying => "retrying",
        DeliveryState.Accepted => "accepted",
        DeliveryState.TerminalFailure => "terminal_failure",
        _ => "obsolete",
    };

    private static DeliveryState ParseState(string value) => value switch
    {
        "queued" => DeliveryState.Queued,
        "claimed" => DeliveryState.Claimed,
        "retrying" => DeliveryState.Retrying,
        "accepted" => DeliveryState.Accepted,
        "terminal_failure" => DeliveryState.TerminalFailure,
        _ => DeliveryState.Obsolete,
    };
}
