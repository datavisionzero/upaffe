namespace Upaffe.Domain.Notifications;

public enum NotificationKind { Alert, Recovery }
public enum DeliveryState { Queued, Claimed, Retrying, Accepted, TerminalFailure, Obsolete }

/// <summary>One durable SMTP intent for one incident, kind, and recipient.</summary>
public sealed class NotificationDelivery
{
    private NotificationDelivery() { }

    private NotificationDelivery(Guid incidentId, NotificationKind kind, string recipient,
        string projectKey, string projectName, string monitorKey, string monitorName,
        string monitorType, string reason, DateTimeOffset occurredAt, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        IncidentId = incidentId;
        Kind = kind;
        Recipient = EmailConfiguration.NormalizeAddress(recipient);
        RecipientKey = Recipient.ToLowerInvariant();
        ProjectKey = projectKey;
        ProjectName = projectName;
        MonitorKey = monitorKey;
        MonitorName = monitorName;
        MonitorType = monitorType;
        Reason = reason;
        OccurredAt = occurredAt;
        CreatedAt = now;
        UpdatedAt = now;
        NextAttemptAt = now;
        State = DeliveryState.Queued;
    }

    public Guid Id { get; private set; }
    public Guid IncidentId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string Recipient { get; private set; } = string.Empty;
    public string RecipientKey { get; private set; } = string.Empty;
    public string ProjectKey { get; private set; } = string.Empty;
    public string ProjectName { get; private set; } = string.Empty;
    public string MonitorKey { get; private set; } = string.Empty;
    public string MonitorName { get; private set; } = string.Empty;
    public string MonitorType { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public DeliveryState State { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public DateTimeOffset? TerminalAt { get; private set; }
    public DateTimeOffset? LeaseUntil { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public string? LastErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static NotificationDelivery Queue(Guid incidentId, NotificationKind kind,
        string recipient, string projectKey, string projectName, string monitorKey,
        string monitorName, string monitorType, string reason,
        DateTimeOffset occurredAt, DateTimeOffset now) =>
        new(incidentId, kind, recipient, projectKey, projectName, monitorKey,
            monitorName, monitorType, reason, occurredAt, now);

    public bool Claim(Guid token, DateTimeOffset now, TimeSpan duration)
    {
        if (State is not (DeliveryState.Queued or DeliveryState.Retrying or DeliveryState.Claimed)
            || NextAttemptAt > now || (State == DeliveryState.Claimed && LeaseUntil > now))
            throw new InvalidOperationException("This delivery is not due for claiming.");
        if (AttemptCount >= 5)
        {
            State = DeliveryState.TerminalFailure;
            TerminalAt = now;
            LastErrorCode ??= "smtp_outcome_unknown";
            LeaseToken = null;
            LeaseUntil = null;
            UpdatedAt = now;
            return false;
        }
        State = DeliveryState.Claimed;
        AttemptCount++;
        LastAttemptAt = now;
        LeaseToken = token;
        LeaseUntil = now.Add(duration);
        UpdatedAt = now;
        return true;
    }

    public bool IsClaimedBy(Guid token) => State == DeliveryState.Claimed && LeaseToken == token;

    public void Complete(Guid token, bool accepted, bool transient, string? failureCode,
        DateTimeOffset now)
    {
        if (!IsClaimedBy(token)) throw new InvalidOperationException("Delivery lease was lost.");
        UpdatedAt = now;
        LeaseToken = null;
        LeaseUntil = null;
        if (accepted)
        {
            State = DeliveryState.Accepted;
            AcceptedAt = now;
            LastErrorCode = null;
            return;
        }

        LastErrorCode = failureCode is "smtp_rejected" or "smtp_authentication_failed" or
            "smtp_tls_failed" or "smtp_timeout" or "smtp_connection_failed" or
            "smtp_protocol_error" or "smtp_not_configured" or "smtp_credentials_missing"
            ? failureCode : "smtp_failed";
        if (!transient || AttemptCount >= 5)
        {
            State = DeliveryState.TerminalFailure;
            TerminalAt = now;
            return;
        }
        State = DeliveryState.Retrying;
        NextAttemptAt = now.Add(TimeSpan.FromMinutes(1 << (AttemptCount - 1)));
    }

    public void Obsolete(DateTimeOffset now)
    {
        if (State is DeliveryState.Accepted or DeliveryState.TerminalFailure or DeliveryState.Obsolete)
            return;
        State = DeliveryState.Obsolete;
        LeaseToken = null;
        LeaseUntil = null;
        TerminalAt = now;
        UpdatedAt = now;
    }
}
