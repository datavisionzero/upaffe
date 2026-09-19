namespace Upaffe.Application.Ports;

public sealed record EmailDeliveryLease(Guid DeliveryId, Guid Token,
    Guid IncidentId, string Kind, string Recipient, string ProjectKey,
    string ProjectName, string MonitorKey, string MonitorName, string MonitorType,
    string Reason, DateTimeOffset OccurredAt);

public enum EmailPreparation { Send, Deferred, Obsolete, LeaseLost }

public interface IEmailDeliveryStore
{
    Task<bool> ReconcileAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<EmailDeliveryLease?> ClaimAsync(DateTimeOffset now, TimeSpan leaseDuration,
        CancellationToken cancellationToken);
    Task<EmailPreparation> PrepareAsync(Guid deliveryId, Guid token,
        DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> CompleteAsync(Guid deliveryId, Guid token, EmailSendResult result,
        DateTimeOffset now, CancellationToken cancellationToken);
}
