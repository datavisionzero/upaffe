using Upaffe.Application.Monitoring;
using Upaffe.Application.Ports;

namespace Upaffe.UnitTests;

public sealed class RunPushDeadlineTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task One_iteration_uses_the_injected_clock_and_completes_the_claimed_deadline()
    {
        var lease = new PushDeadlineLease(Guid.NewGuid(), Guid.NewGuid(), 2, Noon.AddMinutes(-1));
        var store = new RecordingStore(lease);
        var run = new RunPushDeadline(store, new FixedClock(Noon));

        Assert.True(await run.ExecuteOnceAsync(TestContext.Current.CancellationToken));
        Assert.Equal(Noon, store.ClaimedAt);
        Assert.Equal(Noon, store.CompletedAt);
        Assert.Equal(RunPushDeadline.LeaseDuration, store.LeaseDuration);
        Assert.Same(lease, store.CompletedLease);
    }

    [Fact]
    public async Task An_idle_iteration_returns_without_completing_work()
    {
        var store = new RecordingStore(null);
        var run = new RunPushDeadline(store, new FixedClock(Noon));

        Assert.False(await run.ExecuteOnceAsync(TestContext.Current.CancellationToken));
        Assert.Null(store.CompletedLease);
    }

    private sealed class RecordingStore(PushDeadlineLease? lease) : IPushDeadlineStore
    {
        public DateTimeOffset? ClaimedAt { get; private set; }
        public DateTimeOffset? CompletedAt { get; private set; }
        public TimeSpan? LeaseDuration { get; private set; }
        public PushDeadlineLease? CompletedLease { get; private set; }

        public Task<PushDeadlineLease?> ClaimAsync(
            DateTimeOffset now,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            ClaimedAt = now;
            LeaseDuration = leaseDuration;
            return Task.FromResult(lease);
        }

        public Task<PushDeadlineCompletion> CompleteAsync(
            PushDeadlineLease claimed,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            CompletedLease = claimed;
            CompletedAt = now;
            return Task.FromResult(PushDeadlineCompletion.Completed);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
