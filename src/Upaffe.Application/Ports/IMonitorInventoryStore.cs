namespace Upaffe.Application.Ports;

public sealed record MonitorInventoryFilter(string? Project, string? State, string? Type,
    string? Search, int Limit, int Offset);

public sealed record MonitorInventoryItem(
    string ProjectKey, string ProjectName, Guid Id, string Type, string Key,
    string Name, string? Purpose, string? Mode, string? TargetUrl,
    int IntervalSeconds, int? ToleranceSeconds, string State, bool Overdue,
    bool IncidentOpen, DateTimeOffset? LatestObservationAt,
    DateTimeOffset? LastSuccessAt, DateTimeOffset? NextDueAt,
    DateTimeOffset? MaintenanceUntil);

public sealed record MonitorInventoryPage(DateTimeOffset GeneratedAt, int Total,
    int Limit, int Offset, bool HasMore, IReadOnlyList<MonitorInventoryItem> Items);

public interface IMonitorInventoryStore
{
    Task<MonitorInventoryPage> ReadAsync(MonitorInventoryFilter filter,
        DateTimeOffset now, CancellationToken cancellationToken);
}
