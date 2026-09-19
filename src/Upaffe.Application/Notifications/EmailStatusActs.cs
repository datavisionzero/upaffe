using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;
using Upaffe.Domain.Projects;

namespace Upaffe.Application.Notifications;

public sealed class EmailStatusActs(IEmailStatusStore store, TimeProvider clock)
{
    public Task<EmailDeliveryPage> ListAsync(Identity identity, string? projectKey,
        string? monitorType, string? monitorKey, Guid? incidentId, string? state,
        int limit, int offset, CancellationToken cancellationToken)
    {
        _ = identity;
        if (limit is < 1 or > 100 || offset is < 0 or > 10000)
            throw Validation("page", "Limit must be 1-100 and offset 0-10000.");
        if (projectKey is not null)
        {
            try { projectKey = Project.ValidateKey(projectKey); }
            catch (ArgumentException ex) { throw Validation("project_key", ex.Message); }
        }
        if (monitorType is not null && monitorType is not ("http" or "push"))
            throw Validation("monitor_type", "Monitor type must be http or push.");
        if (monitorKey is not null && (projectKey is null || monitorType is null))
            throw Validation("monitor_key", "Monitor key requires project key and monitor type.");
        if (state is not null && state is not ("queued" or "claimed" or "retrying"
            or "accepted" or "terminal_failure" or "obsolete"))
            throw Validation("state", "Unknown stored delivery state.");
        return store.ListAsync(projectKey, monitorType, monitorKey, incidentId,
            state, limit, offset, clock.GetUtcNow(), cancellationToken);
    }

    public async Task<EmailDeliverySummary> SummaryAsync(Identity identity,
        string? projectKey, CancellationToken cancellationToken)
    {
        _ = identity;
        if (projectKey is not null)
        {
            try { projectKey = Project.ValidateKey(projectKey); }
            catch (ArgumentException ex) { throw Validation("project_key", ex.Message); }
            if (!await store.ProjectExistsAsync(projectKey, cancellationToken))
                throw Refusal.NotFound("No such project.");
        }
        return await store.SummaryAsync(projectKey, cancellationToken);
    }

    public async Task<IncidentEmailStatus> ReadIncidentAsync(Identity identity,
        Guid incidentId, string? monitorType, CancellationToken cancellationToken)
    {
        _ = identity;
        if (incidentId == Guid.Empty) throw Validation("incident_id", "An incident ID is required.");
        if (monitorType is not ("http" or "push"))
            throw Validation("monitor_type", "Monitor type must be http or push.");
        return await store.ReadIncidentAsync(incidentId, monitorType,
            clock.GetUtcNow(), cancellationToken)
            ?? throw Refusal.NotFound("No such incident.");
    }

    private static Refusal Validation(string field, string message) =>
        Refusal.Validation(new Dictionary<string, string[]> { [field] = [message] });
}
