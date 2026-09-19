using Upaffe.Application.Ports;

namespace Upaffe.Application.Notifications;

public sealed class RunEmailDelivery(IEmailDeliveryStore deliveries,
    IEmailConfigurationStore settings, IEmailSender sender, TimeProvider clock)
{
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    public async Task<bool> ExecuteOnceAsync(CancellationToken cancellationToken)
    {
        var lease = await deliveries.ClaimAsync(clock.GetUtcNow(), LeaseDuration, cancellationToken);
        if (lease is null) return false;

        var connection = await settings.ReadConnectionAsync(cancellationToken);
        var rendered = IncidentEmailRenderer.Render(new(
            lease.IncidentId, lease.Kind, lease.ProjectKey, lease.ProjectName,
            lease.MonitorKey, lease.MonitorName, lease.MonitorType, lease.Reason,
            lease.OccurredAt, lease.Recipient), connection.PublicBaseUrl);
        var result = await sender.SendAsync(connection, rendered, cancellationToken);
        await deliveries.CompleteAsync(lease.DeliveryId, lease.Token, result,
            clock.GetUtcNow(), cancellationToken);
        return true;
    }
}
