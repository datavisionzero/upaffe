using System.Collections.Concurrent;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Upaffe.Api.Hosting;
using Upaffe.Application.Ports;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class MonitoringProgressTests(PostgresFixture postgres)
{
    private const string PrivateDiagnostic = "fixture-private-target-token";
    private static readonly DateTimeOffset Start =
        new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Freshness_has_a_strict_two_minute_bound_and_recovers_after_clock_changes()
    {
        var clock = new ProgressClock(Start);
        var progress = new MonitoringProgress(clock);
        Assert.False(progress.BothFresh());
        progress.HttpSucceeded();
        Assert.False(progress.BothFresh());
        progress.PushSucceeded();
        Assert.True(progress.BothFresh());

        clock.Advance(MonitoringProgress.Freshness.Subtract(TimeSpan.FromTicks(1)));
        Assert.True(progress.BothFresh());
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.False(progress.BothFresh());
        progress.HttpSucceeded();
        progress.PushSucceeded();
        Assert.True(progress.BothFresh());

        clock.Advance(TimeSpan.FromSeconds(-1));
        Assert.False(progress.BothFresh());
        progress.HttpSucceeded();
        progress.PushSucceeded();
        Assert.True(progress.BothFresh());
    }

    [Fact]
    public async Task Disabled_workers_cannot_make_a_live_ready_server_progressing()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();

        Assert.Equal(HttpStatusCode.OK, await Status(client, "/api/health/live"));
        Assert.Equal(HttpStatusCode.OK, await Status(client, "/api/health/ready"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            await Status(client, "/api/health/progress"));
        using var response = await client.GetAsync(
            "/api/health/progress", TestContext.Current.CancellationToken);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("{\"status\":\"stalled\"}",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Idle_workers_reach_progress_through_real_database_claims()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var instance = AnInstance.Against(connection,
            new Dictionary<string, string?> { ["Monitoring:Enabled"] = "true" });
        using var client = instance.CreateClient();

        await WaitFor(client, HttpStatusCode.OK);
        using var response = await client.GetAsync(
            "/api/health/progress", TestContext.Current.CancellationToken);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("{\"status\":\"progressing\"}",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        await using var context = AnInstance.ContextFor(connection);
        Assert.Equal(0, await context.HttpChecks.CountAsync(
            TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.PushReports.CountAsync(
            TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.Incidents.CountAsync(
            TestContext.Current.CancellationToken));
        Assert.Equal(0, await context.PushIncidents.CountAsync(
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Both_workers_must_complete_a_successful_idle_poll_after_start_and_restart()
    {
        var clock = new ProgressClock(Start);
        var http = new ClaimGate(blocked: true);
        var push = new ClaimGate(blocked: true);
        var (first, connection) = await Instance(clock, http, push);
        await using (first)
        using (var client = first.CreateClient())
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable,
                await Status(client, "/api/health/progress"));
            http.Release();
            Assert.Equal(HttpStatusCode.ServiceUnavailable,
                await Status(client, "/api/health/progress"));
            push.Release();
            await WaitFor(client, HttpStatusCode.OK);

            await using var context = AnInstance.ContextFor(connection);
            Assert.Equal(0, await context.HttpChecks.CountAsync(
                TestContext.Current.CancellationToken));
            Assert.Equal(0, await context.PushReports.CountAsync(
                TestContext.Current.CancellationToken));
        }

        http.Block();
        push.Block();
        await using var second = first.StartedAgain();
        using var restarted = second.CreateClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            await Status(restarted, "/api/health/progress"));
        http.Release();
        push.Release();
        await WaitFor(restarted, HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_stalled_loop_and_failing_claim_stores_go_stale_then_recover()
    {
        var clock = new ProgressClock(Start);
        var http = new ClaimGate();
        var push = new ClaimGate();
        var logs = new FullLoggerProvider();
        var (instance, _) = await Instance(clock, http, push, logs);
        await using (instance)
        using (var client = instance.CreateClient())
        {
            await WaitFor(client, HttpStatusCode.OK);

            http.Fail = true;
            await WaitUntil(() => http.Failures > 0);
            await WaitUntil(() => logs.Messages.Any(message =>
                message.Contains("monitoring loop failed", StringComparison.Ordinal)));
            Assert.DoesNotContain(logs.Messages, message =>
                message.Contains(PrivateDiagnostic, StringComparison.Ordinal));
            var pushBefore = push.Successes;
            clock.Advance(MonitoringProgress.Freshness.Add(TimeSpan.FromSeconds(1)));
            await WaitUntil(() => push.Successes > pushBefore);
            Assert.Equal(HttpStatusCode.ServiceUnavailable,
                await Status(client, "/api/health/progress"));
            Assert.Equal(HttpStatusCode.OK, await Status(client, "/api/health/ready"));

            http.Fail = false;
            await WaitFor(client, HttpStatusCode.OK);

            http.Fail = true;
            push.Fail = true;
            var httpFailures = http.Failures;
            var pushFailures = push.Failures;
            await WaitUntil(() => http.Failures > httpFailures && push.Failures > pushFailures);
            clock.Advance(MonitoringProgress.Freshness.Add(TimeSpan.FromSeconds(1)));
            Assert.Equal(HttpStatusCode.OK, await Status(client, "/api/health/live"));
            Assert.Equal(HttpStatusCode.OK, await Status(client, "/api/health/ready"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable,
                await Status(client, "/api/health/progress"));

            http.Fail = false;
            push.Fail = false;
            await WaitFor(client, HttpStatusCode.OK);
        }
    }

    private async Task<(AnInstance Instance, string Connection)> Instance(
        ProgressClock clock, ClaimGate http, ClaimGate push,
        ILoggerProvider? logs = null)
    {
        var connection = await postgres.CreateDatabaseAsync();
        var settings = new Dictionary<string, string?>
        {
            ["Monitoring:Enabled"] = "true",
        };
        var instance = AnInstance.Against(connection, settings, clock, logs,
            serviceOverrides: services =>
            {
                services.RemoveAll<IScheduledHttpCheckStore>();
                services.AddSingleton<IScheduledHttpCheckStore>(new ControlledHttpStore(http));
                services.RemoveAll<IPushDeadlineStore>();
                services.AddSingleton<IPushDeadlineStore>(new ControlledPushStore(push));
            });
        return (instance, connection);
    }

    private static async Task<HttpStatusCode> Status(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private static async Task WaitFor(HttpClient client, HttpStatusCode expected)
    {
        HttpStatusCode actual = 0;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            actual = await Status(client, "/api/health/progress");
            if (actual == expected)
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Equal(expected, actual);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }

    private sealed class ProgressClock(DateTimeOffset now) : TimeProvider
    {
        private long _ticks = now.UtcDateTime.Ticks;

        public override DateTimeOffset GetUtcNow() =>
            new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

        public void Advance(TimeSpan duration) =>
            Interlocked.Add(ref _ticks, duration.Ticks);
    }

    private sealed class ClaimGate(bool blocked = false)
    {
        private readonly Lock _gate = new();
        private TaskCompletionSource? _release = blocked
            ? new(TaskCreationOptions.RunContinuationsAsynchronously)
            : null;
        private int _fail;
        private int _successes;
        private int _failures;

        public bool Fail
        {
            get => Volatile.Read(ref _fail) != 0;
            set => Interlocked.Exchange(ref _fail, value ? 1 : 0);
        }

        public int Successes => Volatile.Read(ref _successes);
        public int Failures => Volatile.Read(ref _failures);

        public void Block()
        {
            lock (_gate)
            {
                _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public void Release()
        {
            lock (_gate)
            {
                _release?.TrySetResult();
                _release = null;
            }
        }

        public async Task Claim(CancellationToken cancellationToken)
        {
            Task? wait;
            lock (_gate)
            {
                wait = _release?.Task;
            }

            if (wait is not null)
            {
                await wait.WaitAsync(cancellationToken);
            }

            if (Fail)
            {
                Interlocked.Increment(ref _failures);
                throw new InvalidOperationException(PrivateDiagnostic);
            }

            Interlocked.Increment(ref _successes);
        }
    }

    private sealed class ControlledHttpStore(ClaimGate gate) : IScheduledHttpCheckStore
    {
        public async Task<ScheduledHttpCheckLease?> ClaimAsync(
            DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken)
        {
            await gate.Claim(cancellationToken);
            return null;
        }

        public Task<ScheduledHttpCheckCompletion> CompleteAsync(
            Guid checkId, Guid leaseToken, HttpExecutionResult result,
            DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ControlledPushStore(ClaimGate gate) : IPushDeadlineStore
    {
        public async Task<PushDeadlineLease?> ClaimAsync(
            DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken)
        {
            await gate.Claim(cancellationToken);
            return null;
        }

        public Task<PushDeadlineCompletion> CompleteAsync(
            PushDeadlineLease lease, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FullLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new FullLogger(Messages);
        public void Dispose() { }

        private sealed class FullLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception) + exception?.ToString());
        }
    }
}
