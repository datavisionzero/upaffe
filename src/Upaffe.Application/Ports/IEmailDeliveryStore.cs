namespace Upaffe.Application.Ports;

public sealed record EmailDeliveryLease(Guid DeliveryId, Guid Token,
    Guid IncidentId, string Kind, string Recipient, string ProjectKey,
    string ProjectName, string MonitorKey, string MonitorName, string MonitorType,
    string Reason, DateTimeOffset OccurredAt);

public interface IEmailDeliveryStore
{
    Task<EmailDeliveryLease?> ClaimAsync(DateTimeOffset now, TimeSpan leaseDuration,
        CancellationToken cancellationToken);
    Task<bool> CompleteAsync(Guid deliveryId, Guid token, EmailSendResult result,
        DateTimeOffset now, CancellationToken cancellationToken);
}
