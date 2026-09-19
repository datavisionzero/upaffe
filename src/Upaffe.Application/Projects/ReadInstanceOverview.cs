using Upaffe.Application.Ports;
using Upaffe.Domain.Access;

namespace Upaffe.Application.Projects;

public sealed class ReadInstanceOverview(IInstanceOverviewStore store, TimeProvider clock)
{
    public Task<InstanceOverview> ExecuteAsync(Identity identity,
        CancellationToken cancellationToken)
    {
        _ = identity;
        return store.ReadAsync(clock.GetUtcNow(), cancellationToken);
    }
}
