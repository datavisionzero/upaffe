using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;
using Upaffe.Domain.Projects;

namespace Upaffe.Application.Monitoring;

public sealed class ReadMonitorInventory(IMonitorInventoryStore store, TimeProvider clock)
{
    public Task<MonitorInventoryPage> ExecuteAsync(Identity identity,
        string? project, string? state, string? type, string? q,
        int? limit, int? offset, CancellationToken cancellationToken)
    {
        _ = identity;
        var projectKey = string.IsNullOrWhiteSpace(project) ? null : project.Trim();
        if (projectKey is not null)
        {
            try { projectKey = Project.ValidateKey(projectKey); }
            catch (ArgumentException) { throw Invalid("project", "A valid project key is required."); }
        }
        if (state is not null && state is not ("healthy" or "failing" or "untested" or "paused"))
            throw Invalid("state", "State must be healthy, failing, untested, or paused.");
        if (type is not null && type is not ("http" or "push"))
            throw Invalid("type", "Type must be http or push.");
        var search = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        if (search?.Length > 120)
            throw Invalid("q", "Search may contain at most 120 characters.");
        if (limit is < 1 or > 100)
            throw Invalid("limit", "Limit must be between 1 and 100.");
        if (offset is < 0 or > 1_000_000)
            throw Invalid("offset", "Offset must be between 0 and 1000000.");
        return store.ReadAsync(new(projectKey, state, type, search,
            limit ?? 50, offset ?? 0), clock.GetUtcNow(), cancellationToken);
    }

    private static Refusal Invalid(string field, string message) =>
        Refusal.Validation(new Dictionary<string, string[]> { [field] = [message] });
}
