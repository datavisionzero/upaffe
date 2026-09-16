using Upaffe.Application.Monitoring;
using Upaffe.Application.Ports;

namespace Upaffe.UnitTests;

public sealed class HttpMonitorHistoryTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Retention_uses_the_injected_clock_and_the_ninety_day_boundary()
    {
        var store = new RecordingStore();
        var prune = new PruneHttpMonitorHistory(store, new FixedClock(Noon));

        await prune.ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Noon.AddDays(-90), store.Cutoff);
    }

    private sealed class RecordingStore : IHttpMonitorHistoryStore
    {
        public DateTimeOffset? Cutoff { get; private set; }

        public Task<HttpHistoryPruneResult> PruneAsync(
            DateTimeOffset cutoff,
            CancellationToken cancellationToken)
        {
            Cutoff = cutoff;
            return Task.FromResult(new HttpHistoryPruneResult(0, 0));
        }

        public Task<HttpCheckHistoryPage?> ListChecksAsync(
            string projectKey,
            string monitorKey,
            long? beforeSequence,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IncidentHistoryPage?> ListIncidentsAsync(
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
