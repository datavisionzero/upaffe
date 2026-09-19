using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;

namespace Upaffe.Application.Monitoring;

public sealed class ReadPushReportEvidence(IPushMonitorHistoryStore history)
{
    public async Task<PushReportHistoryItem> ExecuteAsync(
        Identity identity, string? projectKey, string? monitorKey, Guid reportId,
        CancellationToken cancellationToken)
    {
        _ = identity;
        return await history.ReadReportAsync(
            PushMonitorValidation.ProjectKey(projectKey),
            PushMonitorValidation.MonitorKey(monitorKey), reportId, cancellationToken)
            ?? throw Refusal.NotFound("No such push report.");
    }
}

public sealed class ReadPushIncidentEvidence(IPushMonitorHistoryStore history)
{
    public async Task<PushIncidentHistoryItem> ExecuteAsync(
        Identity identity, string? projectKey, string? monitorKey, Guid incidentId,
        CancellationToken cancellationToken)
    {
        _ = identity;
        return await history.ReadIncidentAsync(
            PushMonitorValidation.ProjectKey(projectKey),
            PushMonitorValidation.MonitorKey(monitorKey), incidentId, cancellationToken)
            ?? throw Refusal.NotFound("No such push incident.");
    }
}

public sealed class ListPushReportHistory(IPushMonitorHistoryStore history)
{
    public async Task<PushReportHistoryPage> ExecuteAsync(
        Identity identity,
        string? projectKey,
        string? monitorKey,
        long? beforeSequence,
        int? limit,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var page = await history.ListReportsAsync(
            PushMonitorValidation.ProjectKey(projectKey),
            PushMonitorValidation.MonitorKey(monitorKey),
            ListHttpCheckHistory.Cursor(beforeSequence, "before_sequence"),
            ListHttpCheckHistory.PageSize(limit),
            cancellationToken);
        return page ?? throw Refusal.NotFound("No such push monitor.");
    }
}

public sealed class ListPushIncidentHistory(IPushMonitorHistoryStore history)
{
    public async Task<PushIncidentHistoryPage> ExecuteAsync(
        Identity identity,
        string? projectKey,
        string? monitorKey,
        long? beforeOpeningSequence,
        int? limit,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var page = await history.ListIncidentsAsync(
            PushMonitorValidation.ProjectKey(projectKey),
            PushMonitorValidation.MonitorKey(monitorKey),
            ListHttpCheckHistory.Cursor(beforeOpeningSequence, "before_opening_sequence"),
            ListHttpCheckHistory.PageSize(limit),
            cancellationToken);
        return page ?? throw Refusal.NotFound("No such push monitor.");
    }
}

public sealed class PrunePushMonitorHistory(IPushMonitorHistoryStore history, TimeProvider clock)
{
    public Task<PushHistoryPruneResult> ExecuteAsync(CancellationToken cancellationToken) =>
        history.PruneAsync(clock.GetUtcNow().Subtract(HttpMonitorHistoryLimits.Retention), cancellationToken);
}
