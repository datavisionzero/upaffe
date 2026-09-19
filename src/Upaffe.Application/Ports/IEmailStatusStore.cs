namespace Upaffe.Application.Ports;

public sealed record EmailDeliveryStatus(
    Guid Id, Guid IncidentId, string Kind, string Recipient,
    string ProjectKey, string MonitorType, string MonitorKey,
    string State, string? SuppressionReason, int AttemptCount,
    DateTimeOffset? LastAttemptAt, DateTimeOffset? NextAttemptAt,
    DateTimeOffset? AcceptedAt, DateTimeOffset? TerminalAt,
    string? LastErrorCode, DateTimeOffset CreatedAt);

public sealed record EmailDeliveryPage(IReadOnlyList<EmailDeliveryStatus> Items,
    int Total, int Limit, int Offset, bool HasMore);

public sealed record EmailDeliverySummary(string? ProjectKey,
    int PendingCount, int RetryingCount, int TerminalFailureCount,
    int SmtpAcceptedCount, DateTimeOffset? OldestPendingAt);

public sealed record IncidentEmailStatus(Guid IncidentId, string ProjectKey,
    string MonitorType, string MonitorKey, bool Open,
    string AnnouncementState, string? SuppressionReason,
    IReadOnlyList<EmailDeliveryStatus> Deliveries);

public interface IEmailStatusStore
{
    Task<bool> ProjectExistsAsync(string projectKey, CancellationToken cancellationToken);
    Task<EmailDeliveryPage> ListAsync(string? projectKey, string? monitorType,
        string? monitorKey, Guid? incidentId, string? state, int limit, int offset,
        DateTimeOffset now, CancellationToken cancellationToken);
    Task<EmailDeliverySummary> SummaryAsync(string? projectKey,
        CancellationToken cancellationToken);
    Task<IncidentEmailStatus?> ReadIncidentAsync(Guid incidentId, string monitorType,
        DateTimeOffset now, CancellationToken cancellationToken);
}
