using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Upaffe.Api.Hosting;

namespace Upaffe.IntegrationTests;

public sealed class HeartbeatTests
{
    private const string Secret = "fictional-receiver-token";
    private static readonly DateTimeOffset Start =
        new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task No_destination_makes_no_request_even_with_fresh_workers()
    {
        var destination = DeploymentSecrets.Heartbeat(Configuration());
        var clock = new TestClock();
        var progress = FreshProgress(clock);
        var receiver = new FakeReceiver();
        using var client = new HttpClient(receiver);
        var sender = new HeartbeatSender(destination, progress, client,
            new TestLogger<HeartbeatSender>());

        Assert.False(sender.Enabled);
        await sender.SendIfProgressingAsync(TestContext.Current.CancellationToken);
        Assert.Empty(receiver.Requests);
    }

    [Fact]
    public async Task A_minimal_signal_follows_both_workers_and_stops_at_the_freshness_bound()
    {
        var clock = new TestClock();
        var progress = new MonitoringProgress(clock);
        var receiver = new FakeReceiver();
        var logger = new TestLogger<HeartbeatSender>();
        using var client = new HttpClient(receiver);
        var sender = new HeartbeatSender(Destination(), progress, client, logger);

        await sender.SendIfProgressingAsync(TestContext.Current.CancellationToken);
        progress.HttpSucceeded();
        await sender.SendIfProgressingAsync(TestContext.Current.CancellationToken);
        Assert.Empty(receiver.Requests);
        progress.PushSucceeded();
        await sender.SendIfProgressingAsync(TestContext.Current.CancellationToken);
        await sender.SendIfProgressingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, receiver.Requests.Count);
        Assert.All(receiver.Requests, request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"https://receiver.example.test/ping/{Secret}?key={Secret}",
                request.Url);
            Assert.False(request.HasContent);
            Assert.Empty(request.Headers);
        });

        clock.Advance(MonitoringProgress.Freshness);
        await sender.SendIfProgressingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, receiver.Requests.Count);
        progress.HttpSucceeded();
        await sender.SendIfProgressingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, receiver.Requests.Count);
        progress.PushSucceeded();
        await sender.SendIfProgressingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, receiver.Requests.Count);
        Assert.Empty(logger.Messages);
    }

    [Fact]
    public async Task Receiver_errors_timeouts_and_response_bodies_never_expose_a_secret()
    {
        var clock = new TestClock();
        var progress = FreshProgress(clock);
        var logger = new TestLogger<HeartbeatSender>();
        var receiver = new FakeReceiver
        {
            Reply = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent($"untrusted {Secret}"),
            }),
        };
        using var client = new HttpClient(receiver) { Timeout = TimeSpan.FromMilliseconds(50) };
        var sender = new HeartbeatSender(Destination(), progress, client, logger);
        await sender.SendIfProgressingAsync(TestContext.Current.CancellationToken);
        Assert.Single(receiver.Requests);
        Assert.Contains(logger.Messages, message => message.Contains("502", StringComparison.Ordinal));

        receiver.Reply = async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        await sender.SendIfProgressingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, receiver.Requests.Count);
        Assert.DoesNotContain(logger.Messages, message =>
            message.Contains(Secret, StringComparison.Ordinal)
            || message.Contains("receiver.example.test", StringComparison.Ordinal)
            || message.Contains("untrusted", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://receiver.example.test/ping")]
    [InlineData("https://localhost/ping")]
    [InlineData("https://127.0.0.1/ping")]
    [InlineData("https://receiver.local/ping")]
    [InlineData("https://user:pass@receiver.example.test/ping")]
    [InlineData("https://receiver.example.test/ping#fragment")]
    [InlineData("https://receiver.example.test/ping\nsecond-line")]
    public void Invalid_destinations_fail_without_reporting_the_value_or_path(string value)
    {
        var path = TemporaryFile(value);
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                DeploymentSecrets.Heartbeat(Configuration(path)));
            Assert.Contains(DeploymentSecrets.HeartbeatUrlFile, error.Message,
                StringComparison.Ordinal);
            Assert.DoesNotContain(value, error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(path, error.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Valid_destination_is_file_backed_and_redacted_in_diagnostics()
    {
        var path = TemporaryFile($"https://receiver.example.test/ping/{Secret}?key={Secret}\n");
        try
        {
            var destination = DeploymentSecrets.Heartbeat(Configuration(path));
            Assert.Equal(Uri.UriSchemeHttps, destination.Url?.Scheme);
            Assert.DoesNotContain(Secret, destination.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(path, destination.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static HeartbeatDestination Destination() => new(
        new Uri($"https://receiver.example.test/ping/{Secret}?key={Secret}"));

    private static MonitoringProgress FreshProgress(TestClock clock)
    {
        var progress = new MonitoringProgress(clock);
        progress.HttpSucceeded();
        progress.PushSucceeded();
        return progress;
    }

    private static IConfiguration Configuration(string? path = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [DeploymentSecrets.HeartbeatUrlFile] = path,
        }).Build();

    private static string TemporaryFile(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"upaffe-heartbeat-test-{Guid.NewGuid():N}");
        File.WriteAllText(path, contents);
        return path;
    }

    private sealed class TestClock : TimeProvider
    {
        private long _ticks = Start.UtcDateTime.Ticks;
        public override DateTimeOffset GetUtcNow() =>
            new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
    }

    private sealed record Request(HttpMethod Method, string? Url, bool HasContent,
        string[] Headers);

    private sealed class FakeReceiver : HttpMessageHandler
    {
        public ConcurrentQueue<Request> Requests { get; } = new();
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Reply
            { get; set; } = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(new Request(request.Method, request.RequestUri?.AbsoluteUri,
                request.Content is not null,
                request.Headers.Select(header => header.Key).ToArray()));
            return Reply(request, cancellationToken);
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Enqueue(formatter(state, exception) + exception?.ToString());
    }
}
