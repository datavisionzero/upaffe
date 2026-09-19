using Upaffe.Application.Monitoring;
using Upaffe.Application.Ports;

namespace Upaffe.UnitTests;

public sealed class PushMonitorHistoryTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Retention_uses_the_injected_clock_and_the_shared_ninety_day_boundary()
    {
        var store = new RecordingStore();
        var prune = new PrunePushMonitorHistory(store, new FixedClock(Noon));

        await prune.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Noon.AddDays(-90), store.Cutoff);
    }

    private sealed class RecordingStore : IPushMonitorHistoryStore
    {
        public DateTimeOffset? Cutoff { get; private set; }

        public Task<PushReportHistoryItem?> ReadReportAsync(
            string projectKey, string monitorKey, Guid reportId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PushIncidentHistoryItem?> ReadIncidentAsync(
            string projectKey, string monitorKey, Guid incidentId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PushHistoryPruneResult> PruneAsync(
            DateTimeOffset cutoff,
            CancellationToken cancellationToken)
        {
            Cutoff = cutoff;
            return Task.FromResult(new PushHistoryPruneResult(0, 0));
        }

        public Task<PushReportHistoryPage?> ListReportsAsync(
            string projectKey,
            string monitorKey,
            long? beforeSequence,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PushIncidentHistoryPage?> ListIncidentsAsync(
            string projectKey,
            string monitorKey,
            long? beforeOpeningSequence,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
