namespace Upaffe.Application.Ports;

public sealed record EmailHistoryPruneResult(int DeliveriesDeleted, int WindowsDeleted);

public interface IEmailHistoryStore
{
    Task<EmailHistoryPruneResult> PruneAsync(DateTimeOffset cutoff,
        CancellationToken cancellationToken);
}
