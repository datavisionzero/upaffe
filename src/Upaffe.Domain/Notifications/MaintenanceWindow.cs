namespace Upaffe.Domain.Notifications;

/// <summary>One finite suppression period for one project or monitor.</summary>
public sealed class MaintenanceWindow
{
    private MaintenanceWindow() { }

    private MaintenanceWindow(string scopeType, Guid scopeId, long version,
        DateTimeOffset now, TimeSpan duration)
    {
        Id = Guid.NewGuid();
        ScopeType = scopeType;
        ScopeId = scopeId;
        Version = version;
        StartedAt = now;
        EndsAt = now.Add(duration);
    }

    public Guid Id { get; private set; }
    public string ScopeType { get; private set; } = string.Empty;
    public Guid ScopeId { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => EndedAt is null && EndsAt > now;

    public static MaintenanceWindow Start(string scopeType, Guid scopeId, long version,
        DateTimeOffset now, TimeSpan duration)
    {
        ValidateDuration(duration);
        if (scopeType is not ("project" or "http" or "push") || version <= 0)
            throw new ArgumentException("Invalid maintenance scope or version.");
        return new(scopeType, scopeId, version, now, duration);
    }

    public void Extend(DateTimeOffset now, TimeSpan duration)
    {
        ValidateDuration(duration);
        if (!IsActive(now)) throw new InvalidOperationException("Maintenance is no longer active.");
        var requestedEnd = now.Add(duration);
        if (requestedEnd > EndsAt) EndsAt = requestedEnd;
        Version++;
    }

    public void End(DateTimeOffset now)
    {
        if (!IsActive(now)) throw new InvalidOperationException("Maintenance is no longer active.");
        EndedAt = now;
        Version++;
    }

    public static void ValidateDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromMinutes(1) || duration > TimeSpan.FromDays(30))
            throw new ArgumentException("Maintenance duration must be 1 minute through 30 days.", nameof(duration));
    }
}
