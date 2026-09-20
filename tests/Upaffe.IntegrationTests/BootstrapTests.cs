using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Upaffe.Api.Hosting;
using Upaffe.Api.Http;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class BootstrapTests(PostgresFixture postgres)
{
    private const string Password = "a synthetic operator password";

    [Fact]
    public async Task Local_bootstrap_migrates_and_issues_an_usable_management_token()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var instance = AnInstance.Against(connection);
        var (code, output) = await RunAsync(connection,
            ["bootstrap", "--email", "operator@example.test", "--credential-name", "first agent", "--password-stdin"],
            Password + "\n");
        Assert.Equal(0, code);
        using var result = JsonDocument.Parse(output);
        Assert.Equal("created", result.RootElement.GetProperty("status").GetString());
        var token = result.RootElement.GetProperty("token").GetString();
        Assert.StartsWith("upaffe_", token, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, output, StringComparison.Ordinal);

        using var client = instance.CreateClient();
        Assert.Equal(new BootstrapStateResponse(false),
            await client.GetFromJsonAsync<BootstrapStateResponse>("/api/bootstrap", TestContext.Current.CancellationToken));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/projects");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var context = AnInstance.ContextFor(connection);
        var stored = await context.Operators.SingleAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("$argon2id$", stored.PasswordHash, StringComparison.Ordinal);
        Assert.Single(await context.ManagementCredentials.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Single(await context.ManagementCredentialSecrets.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Invalid_input_and_removed_http_write_do_not_initialize_the_instance()
    {
        var connection = await postgres.CreateDatabaseAsync();
        var (invalid, output) = await RunAsync(connection,
            ["bootstrap", "--email", "invalid", "--credential-name", "", "--password-stdin"], "short\n");
        Assert.Equal(2, invalid);
        Assert.Contains("invalid_input", output, StringComparison.Ordinal);
        Assert.DoesNotContain("short", output, StringComparison.Ordinal);

        await using var instance = AnInstance.Against(connection);
        using var client = instance.CreateClient();
        using var rejected = await client.PostAsJsonAsync("/api/bootstrap",
            new { email = "operator@example.test", password = Password }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        Assert.Equal(new BootstrapStateResponse(true),
            await client.GetFromJsonAsync<BootstrapStateResponse>("/api/bootstrap", TestContext.Current.CancellationToken));
        await using var context = AnInstance.ContextFor(connection);
        Assert.Empty(await context.Operators.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Concurrent_and_repeated_commands_create_only_one_operator_and_one_credential()
    {
        var connection = await postgres.CreateDatabaseAsync();
        var args = new[] { "bootstrap", "--email", "operator@example.test", "--credential-name", "first agent", "--password-stdin" };
        var attempts = await Task.WhenAll(
            RunAsync(connection, args, Password + "\n"),
            RunAsync(connection, args, Password + "\n"));
        Assert.Equal([0, 3], attempts.Select(item => item.Code).Order().ToArray());
        Assert.Equal(1, attempts.Count(item => item.Output.Contains("\"token\"", StringComparison.Ordinal)));

        var repeated = await RunAsync(connection, args, "");
        Assert.Equal(3, repeated.Code);
        Assert.Equal("{\"status\":\"already_initialized\"}\n", repeated.Output);
        await using var context = AnInstance.ContextFor(connection);
        Assert.Equal(1, await context.Operators.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.ManagementCredentials.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Lost_output_can_be_recovered_locally_without_reopening_bootstrap()
    {
        var connection = await postgres.CreateDatabaseAsync();
        var initial = await RunAsync(connection,
            ["bootstrap", "--email", "operator@example.test", "--credential-name", "first agent", "--password-stdin"],
            Password + "\n");
        Assert.Equal(0, initial.Code);
        using var original = JsonDocument.Parse(initial.Output);
        var oldToken = original.RootElement.GetProperty("token").GetString();
        var recovery = await RunAsync(connection, ["recover-credential", "--name", "first agent"], "");
        Assert.Equal(0, recovery.Code);
        using var recovered = JsonDocument.Parse(recovery.Output);
        Assert.Equal("recovered", recovered.RootElement.GetProperty("status").GetString());
        var replacement = recovered.RootElement.GetProperty("token").GetString();
        Assert.StartsWith("upaffe_", replacement, StringComparison.Ordinal);
        Assert.DoesNotContain(replacement!, initial.Output, StringComparison.Ordinal);

        await using var instance = AnInstance.Against(connection);
        using var client = instance.CreateClient();
        Assert.Equal(new BootstrapStateResponse(false),
            await client.GetFromJsonAsync<BootstrapStateResponse>("/api/bootstrap", TestContext.Current.CancellationToken));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/projects");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", replacement);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var oldRequest = new HttpRequestMessage(HttpMethod.Get, "/api/projects");
        oldRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", oldToken);
        using var oldResponse = await client.SendAsync(oldRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, oldResponse.StatusCode);
        await using var context = AnInstance.ContextFor(connection);
        Assert.Equal(1, await context.Operators.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.ManagementCredentials.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_failed_output_write_can_be_recovered_without_a_second_operator()
    {
        var connection = await postgres.CreateDatabaseAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = connection }).Build();
        var code = await LocalAccessCommand.RunAsync(
            ["bootstrap", "--email", "operator@example.test", "--credential-name", "first agent", "--password-stdin"],
            configuration, new StringReader(Password + "\n"), new FailingWriter(),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, code);
        var replacement = await RunAsync(connection, ["recover-credential", "--name", "first agent"], "");
        Assert.Equal(0, replacement.Code);
        Assert.Contains("\"status\":\"recovered\"", replacement.Output, StringComparison.Ordinal);
        await using var context = AnInstance.ContextFor(connection);
        Assert.Equal(1, await context.Operators.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.ManagementCredentials.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Protected_password_file_is_accepted_without_exposing_its_path_or_value()
    {
        if (OperatingSystem.IsWindows()) return;
        var connection = await postgres.CreateDatabaseAsync();
        var path = Path.Combine(Path.GetTempPath(), $"upaffe-password-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(path, Password + "\n", TestContext.Current.CancellationToken);
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
            var exposed = await RunAsync(connection,
                ["bootstrap", "--email", "operator@example.test", "--credential-name", "first agent", "--password-file", path], "");
            Assert.Equal(2, exposed.Code);
            Assert.DoesNotContain(path, exposed.Output, StringComparison.Ordinal);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var result = await RunAsync(connection,
                ["bootstrap", "--email", "operator@example.test", "--credential-name", "first agent", "--password-file", path], "");
            Assert.Equal(0, result.Code);
            Assert.DoesNotContain(Password, result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(path, result.Output, StringComparison.Ordinal);
        }
        finally { File.Delete(path); }
    }

    private static async Task<(int Code, string Output)> RunAsync(string connection, string[] args, string input)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = connection }).Build();
        using var output = new StringWriter();
        var code = await LocalAccessCommand.RunAsync(args, configuration, new StringReader(input), output,
            TestContext.Current.CancellationToken);
        return (code, output.ToString());
    }
}

internal sealed class FailingWriter : TextWriter
{
    public override Encoding Encoding => Encoding.UTF8;
    public override Task WriteLineAsync(string? value) => Task.FromException(new IOException("Output unavailable."));
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
    public void Dispose() { }

    private sealed class CollectingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Enqueue(formatter(state, exception));
    }
}
