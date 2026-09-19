namespace Upaffe.Application.Ports;

public sealed record OverviewCounts(int Total, int Healthy, int Failing, int Untested,
    int Paused, int Overdue);
public sealed record OverviewDelivery(int Pending, int Overdue, int Retrying,
    int TerminalFailure, int Accepted, DateTimeOffset? OldestPendingAt);
public sealed record OverviewMonitor(string ProjectKey, string Type, string Key,
    string Name, string State, string? Mode, bool Overdue,
    DateTimeOffset? NextDueAt, DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LatestResultAt, string? LatestOutcome, string? LatestReason,
    Guid? IncidentId, DateTimeOffset? IncidentBeganAt, string? IncidentReason,
    DateTimeOffset? EffectiveMaintenanceUntil);
public sealed record OverviewProject(string Key, string Name, OverviewCounts Counts,
    OverviewDelivery Delivery, DateTimeOffset? MaintenanceUntil);
public sealed record InstanceOverview(DateTimeOffset GeneratedAt, OverviewCounts Counts,
    OverviewDelivery Delivery, IReadOnlyList<OverviewMonitor> Attention,
    IReadOnlyList<OverviewProject> Projects);

public interface IInstanceOverviewStore
{
    Task<InstanceOverview> ReadAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
