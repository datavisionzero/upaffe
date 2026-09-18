using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;

namespace Upaffe.Application.Monitoring;

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
