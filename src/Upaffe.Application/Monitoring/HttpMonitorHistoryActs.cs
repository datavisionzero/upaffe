using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;

namespace Upaffe.Application.Monitoring;

public static class HttpMonitorHistoryLimits
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 100;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);
}

public sealed class ReadHttpCheckEvidence(IHttpMonitorHistoryStore history)
{
    public async Task<HttpCheckHistoryItem> ExecuteAsync(
        Identity identity, string? projectKey, string? monitorKey, Guid checkId,
        CancellationToken cancellationToken)
    {
        _ = identity;
        return await history.ReadCheckAsync(
            HttpMonitorValidation.ProjectKey(projectKey),
            HttpMonitorValidation.MonitorKey(monitorKey), checkId, cancellationToken)
            ?? throw Refusal.NotFound("No such completed HTTP check.");
    }
}

public sealed class ReadHttpIncidentEvidence(IHttpMonitorHistoryStore history)
{
    public async Task<IncidentHistoryItem> ExecuteAsync(
        Identity identity, string? projectKey, string? monitorKey, Guid incidentId,
        CancellationToken cancellationToken)
    {
        _ = identity;
        return await history.ReadIncidentAsync(
            HttpMonitorValidation.ProjectKey(projectKey),
            HttpMonitorValidation.MonitorKey(monitorKey), incidentId, cancellationToken)
            ?? throw Refusal.NotFound("No such HTTP incident.");
    }
}

public sealed class ListHttpCheckHistory(IHttpMonitorHistoryStore history)
{
    public async Task<HttpCheckHistoryPage> ExecuteAsync(
        Identity identity,
        string? projectKey,
        string? monitorKey,
        long? beforeSequence,
        int? limit,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var page = await history.ListChecksAsync(
            HttpMonitorValidation.ProjectKey(projectKey),
            HttpMonitorValidation.MonitorKey(monitorKey),
            Cursor(beforeSequence, "before_sequence"),
            PageSize(limit),
            cancellationToken);
        return page ?? throw Refusal.NotFound("No such HTTP monitor.");
    }

    internal static int PageSize(int? value)
    {
        var accepted = value ?? HttpMonitorHistoryLimits.DefaultPageSize;
        if (accepted is < 1 or > HttpMonitorHistoryLimits.MaximumPageSize)
        {
            throw Refusal.Validation(new Dictionary<string, string[]>
            {
                ["limit"] = [$"Limit must be between 1 and {HttpMonitorHistoryLimits.MaximumPageSize}."],
            });
        }

        return accepted;
    }

    internal static long? Cursor(long? value, string field)
    {
        if (value is <= 0)
        {
            throw Refusal.Validation(new Dictionary<string, string[]>
            {
                [field] = ["A history cursor must be positive."],
            });
        }

        return value;
    }
}

public sealed class ListIncidentHistory(IHttpMonitorHistoryStore history)
{
    public async Task<IncidentHistoryPage> ExecuteAsync(
        Identity identity,
        string? projectKey,
        string? monitorKey,
        long? beforeOpeningSequence,
        int? limit,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var page = await history.ListIncidentsAsync(
            HttpMonitorValidation.ProjectKey(projectKey),
            HttpMonitorValidation.MonitorKey(monitorKey),
            ListHttpCheckHistory.Cursor(beforeOpeningSequence, "before_opening_sequence"),
            ListHttpCheckHistory.PageSize(limit),
            cancellationToken);
        return page ?? throw Refusal.NotFound("No such HTTP monitor.");
    }
}

public sealed class PruneHttpMonitorHistory(IHttpMonitorHistoryStore history, TimeProvider clock)
{
    public Task<HttpHistoryPruneResult> ExecuteAsync(CancellationToken cancellationToken) =>
        history.PruneAsync(clock.GetUtcNow().Subtract(HttpMonitorHistoryLimits.Retention), cancellationToken);
}
