using Upaffe.Application.Monitoring;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;

namespace Upaffe.UnitTests;

public sealed class RunScheduledHttpCheckTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task One_iteration_uses_the_injected_clock_and_completes_the_claimed_work()
    {
        var request = new HttpExecutionRequest(
            "https://status.example.test/health",
            [],
            200,
            TextCondition.None,
            null,
            10);
        var store = new RecordingStore(new(Guid.NewGuid(), Guid.NewGuid(), request));
        var executor = new StubExecutor(new(
            true,
            null,
            "The check succeeded.",
            200,
            12,
            "https://status.example.test/health"));
        var run = new RunScheduledHttpCheck(store, executor, new FixedClock(Noon));

        Assert.True(await run.ExecuteOnceAsync(TestContext.Current.CancellationToken));

        Assert.Equal(Noon, store.ClaimedAt);
        Assert.Equal(Noon, store.CompletedAt);
        Assert.Equal(RunScheduledHttpCheck.LeaseDuration, store.LeaseDuration);
        Assert.Same(request, executor.Request);
    }

    [Fact]
    public async Task An_idle_iteration_returns_without_executing_or_waiting()
    {
        var store = new RecordingStore(null);
        var executor = new StubExecutor(new(false, "unused", "Unused.", null, 0, null));
        var run = new RunScheduledHttpCheck(store, executor, new FixedClock(Noon));

        Assert.False(await run.ExecuteOnceAsync(TestContext.Current.CancellationToken));
        Assert.Null(executor.Request);
    }

    private sealed class RecordingStore(ScheduledHttpCheckLease? lease) : IScheduledHttpCheckStore
    {
        public DateTimeOffset? ClaimedAt { get; private set; }
        public DateTimeOffset? CompletedAt { get; private set; }
        public TimeSpan? LeaseDuration { get; private set; }

        public Task<ScheduledHttpCheckLease?> ClaimAsync(
            DateTimeOffset now,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            ClaimedAt = now;
            LeaseDuration = leaseDuration;
            return Task.FromResult(lease);
        }

        public Task<ScheduledHttpCheckCompletion> CompleteAsync(
            Guid checkId,
            Guid leaseToken,
            HttpExecutionResult result,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            CompletedAt = now;
            return Task.FromResult(ScheduledHttpCheckCompletion.Completed);
        }
    }

    private sealed class StubExecutor(HttpExecutionResult result) : IHttpCheckExecutor
    {
        public HttpExecutionRequest? Request { get; private set; }

        public Task<HttpExecutionResult> ExecuteAsync(
            HttpExecutionRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(result);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
