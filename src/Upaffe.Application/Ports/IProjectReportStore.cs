namespace Upaffe.Application.Ports;

public sealed record ReportProject(Guid Id, string Key, string Name, long Version,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record ReportCounts(int Total, int Http, int Push, int Healthy,
    int Failing, int Untested, int Paused);
public sealed record ReportResult(Guid Id, string Outcome, string? Reason,
    DateTimeOffset ObservedAt, DateTimeOffset? ReceivedAt);
public sealed record ReportIncident(Guid Id, DateTimeOffset BeganAt,
    DateTimeOffset OpenedAt, long AgeSeconds, string OriginalReason,
    string LatestReason);
public sealed record ReportMaintenance(DateTimeOffset StartedAt, DateTimeOffset EndsAt);
public sealed record ReportMonitor(
    string Type, Guid Id, string Key, string Name, string State, string? Mode,
    long Version, string? TargetUrl, int? ExpectedStatusCode,
    string? TextCondition, int IntervalSeconds, int? TimeoutSeconds,
    int? FailureThreshold, int? FailureCount, int? ToleranceSeconds,
    DateTimeOffset? LastReceivedAt, DateTimeOffset? NextDueAt, bool Overdue,
    ReportResult? LatestResult, ReportResult? LastSuccess,
    ReportIncident? Incident, ReportMaintenance? DirectMaintenance,
    DateTimeOffset? EffectiveMaintenanceUntil, string? Instruction,
    string? RunbookUrl);
public sealed record ReportHealthyMonitor(string Type, Guid Id, string Key,
    string Name, string? Mode, DateTimeOffset? LastSuccessAt,
    DateTimeOffset? NextDueAt, ReportMaintenance? DirectMaintenance,
    DateTimeOffset? EffectiveMaintenanceUntil);
public sealed record ReportEmail(bool Configured, string? Host, int? Port,
    string? Security, string? SenderAddress, string? PublicBaseUrl,
    bool HasPassword, IReadOnlyList<string> Recipients,
    EmailDeliverySummary Delivery);
public sealed record ProjectReport(DateTimeOffset GeneratedAt, ReportProject Project,
    ReportCounts Counts, IReadOnlyList<ReportMonitor> Attention,
    IReadOnlyList<ReportHealthyMonitor> Healthy,
    ReportMaintenance? ProjectMaintenance, ReportEmail Email);

public interface IProjectReportStore
{
    Task<ProjectReport?> ReadAsync(string projectKey, DateTimeOffset now,
        CancellationToken cancellationToken);
}
