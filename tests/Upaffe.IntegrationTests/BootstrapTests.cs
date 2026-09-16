using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Upaffe.Api.Http;
using Upaffe.Application.Access;
using Upaffe.Domain.Access;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class BootstrapTests(PostgresFixture postgres)
{
    private const string Proof = "a-bootstrap-proof-with-at-least-thirty-two-characters";
    private const string Password = "a long operator password";

    [Fact]
    public async Task A_fresh_instance_is_established_once_and_returns_no_secret()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var instance = AnInstance.Against(connection, Settings(Proof));
        using var client = instance.CreateClient();

        var before = await client.GetFromJsonAsync<BootstrapStateResponse>(
            "/api/bootstrap", TestContext.Current.CancellationToken);
        Assert.Equal(new BootstrapStateResponse(true, true), before);

        using var success = await client.PostAsJsonAsync(
            "/api/bootstrap",
            new BootstrapRequest(Proof, "operator@example.test", Password),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
        Assert.Empty(await success.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        using var repeated = await client.PostAsJsonAsync(
            "/api/bootstrap",
            new BootstrapRequest(Proof, "other@example.test", Password),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        var problem = await repeated.Content.ReadFromJsonAsync<ProblemResponse>(
            TestContext.Current.CancellationToken);
        Assert.Equal("bootstrap_closed", problem?.Code);

        await using var context = AnInstance.ContextFor(connection);
        var stored = await context.Operators.SingleAsync(TestContext.Current.CancellationToken);
        var grant = await context.BootstrapGrants.SingleAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("$argon2id$", stored.PasswordHash, StringComparison.Ordinal);
        Assert.NotEqual(Password, stored.PasswordHash);
        Assert.NotNull(grant.ConsumedAt);
    }

    [Fact]
    public async Task A_wrong_or_expired_proof_has_the_same_bounded_answer()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        await using var instance = await AnInstance.StartedAsync(postgres, Settings(Proof), clock);
        using var client = instance.CreateClient();

        using var wrong = await client.PostAsJsonAsync(
            "/api/bootstrap",
            new BootstrapRequest("another-proof-that-is-long-enough-to-check", "operator@example.test", Password),
            TestContext.Current.CancellationToken);
        clock.Advance(BootstrapGrant.Lifetime);
        using var expired = await client.PostAsJsonAsync(
            "/api/bootstrap",
            new BootstrapRequest(Proof, "operator@example.test", Password),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal(
            "bootstrap_rejected",
            (await wrong.Content.ReadFromJsonAsync<ProblemResponse>(TestContext.Current.CancellationToken))?.Code);
        Assert.Equal(
            "bootstrap_rejected",
            (await expired.Content.ReadFromJsonAsync<ProblemResponse>(TestContext.Current.CancellationToken))?.Code);
    }

    [Fact]
    public async Task Concurrent_bootstrap_creates_one_operator()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var instance = AnInstance.Against(connection, Settings(Proof));
        using var first = instance.CreateClient();
        using var second = instance.CreateClient();
        var request = new BootstrapRequest(Proof, "operator@example.test", Password);

        var responses = await Task.WhenAll(
            first.PostAsJsonAsync("/api/bootstrap", request, TestContext.Current.CancellationToken),
            second.PostAsJsonAsync("/api/bootstrap", request, TestContext.Current.CancellationToken));

        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        await using var context = AnInstance.ContextFor(connection);
        Assert.Equal(1, await context.Operators.CountAsync(TestContext.Current.CancellationToken));
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Proof_and_password_are_absent_from_responses_and_logs()
    {
        var logs = new CollectingLoggerProvider();
        await using var instance = await AnInstance.StartedAsync(postgres, Settings(Proof), logProvider: logs);
        using var client = instance.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/bootstrap",
            new BootstrapRequest(Proof, "operator@example.test", Password),
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(Proof, body, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, body, StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Messages, message => message.Contains(Proof, StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Messages, message => message.Contains(Password, StringComparison.Ordinal));
    }

    private static Dictionary<string, string?> Settings(string? proof) => new()
    {
        [BootstrapSettings.Variable] = proof,
    };
}

internal sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now = now.Add(duration);
}

internal sealed class CollectingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CollectingLogger(Messages);
    public void Dispose()
    {
    }

    private sealed class CollectingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
    }
}
