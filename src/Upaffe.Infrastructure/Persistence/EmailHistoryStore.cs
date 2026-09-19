using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Domain.Notifications;

namespace Upaffe.Infrastructure.Persistence;

public sealed class EmailHistoryStore(UpaffeDbContext context) : IEmailHistoryStore
{
    public async Task<EmailHistoryPruneResult> PruneAsync(DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        var deliveries = await context.NotificationDeliveries.Where(value =>
            ((value.State == DeliveryState.Accepted && value.AcceptedAt < cutoff)
             || ((value.State == DeliveryState.TerminalFailure
                  || value.State == DeliveryState.Obsolete) && value.TerminalAt < cutoff))
            && (value.Kind != NotificationKind.Alert || value.State != DeliveryState.Accepted
                || value.RecoveryDecisionAt != null)
            && ((value.MonitorType == "http" && !context.Incidents.Any(incident =>
                    incident.Id == value.IncidentId && incident.ResolvedAt == null))
                || (value.MonitorType == "push" && !context.PushIncidents.Any(incident =>
                    incident.Id == value.IncidentId && incident.ResolvedAt == null))))
            .ExecuteDeleteAsync(cancellationToken);
        var windows = await context.MaintenanceWindows.Where(value =>
            (value.EndedAt ?? value.EndsAt) < cutoff
            && context.MaintenanceWindows.Any(later =>
                later.ScopeType == value.ScopeType && later.ScopeId == value.ScopeId
                && later.Version > value.Version))
            .ExecuteDeleteAsync(cancellationToken);
        return new(deliveries, windows);
    }
}
