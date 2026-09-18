namespace Upaffe.Domain.Monitoring;

/// <summary>The revocable identity allowed to report for one push monitor.</summary>
public sealed class ReportingCredential
{
    private ReportingCredential()
    {
    }

    private ReportingCredential(Guid monitorId, DateTimeOffset now)
    {
        if (monitorId == Guid.Empty)
        {
            throw new ArgumentException("A push monitor is required.", nameof(monitorId));
        }

        Id = Guid.NewGuid();
        MonitorId = monitorId;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid MonitorId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RotatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static ReportingCredential Create(Guid monitorId, DateTimeOffset now) => new(monitorId, now);

    public void RecordRotation(DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            throw new InvalidOperationException("A revoked reporting credential cannot be rotated.");
        }

        RotatedAt = now;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
