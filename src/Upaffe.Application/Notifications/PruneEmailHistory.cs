using Upaffe.Application.Ports;

namespace Upaffe.Application.Notifications;

public sealed class PruneEmailHistory(IEmailHistoryStore history, TimeProvider clock)
{
    public Task<EmailHistoryPruneResult> ExecuteAsync(CancellationToken cancellationToken) =>
        history.PruneAsync(clock.GetUtcNow().AddDays(-90), cancellationToken);
}
