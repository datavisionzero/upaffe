using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Upaffe.Api.Http;
using Upaffe.Application.Access;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class PushReportTests(PostgresFixture postgres)
{
    private const string Proof = "a-push-report-bootstrap-proof-with-enough-entropy";
    private static readonly DateTimeOffset Noon = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Fact]
    public async Task JSON_reports_are_monitor_bound_idempotent_and_ordered()
    {
        var clock = new MutableTimeProvider(Noon);
        var setup = await EstablishedAsync(clock);
        await using var instance = setup.Instance;
        using var client = setup.Client;

        var firstId = Guid.NewGuid();
        var first = await Submit(client, setup.ReportingToken, firstId, Noon.AddMinutes(-3), "success");
        Assert.True(first.Applied);
        Assert.False(first.Duplicate);
        Assert.Equal(1, first.Sequence);

        var replay = await Submit(client, setup.ReportingToken, firstId, Noon.AddMinutes(-3), "success");
        Assert.True(replay.Duplicate);
        Assert.Equal(first.ReceivedAt, replay.ReceivedAt);
        Assert.Equal(first.Sequence, replay.Sequence);

        using (var collision = await JsonAsync(client, HttpMethod.Post, "/api/reports",
            new SubmitPushReportRequest(firstId, Noon.AddMinutes(-3), "failure", "different"), setup.ReportingToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
            Assert.Equal("report_id_conflict", (await collision.Content.ReadFromJsonAsync<ProblemResponse>(Json, TestContext.Current.CancellationToken))!.Code);
        }

        var failure = await Submit(client, setup.ReportingToken, Guid.NewGuid(), Noon.AddMinutes(-1), "failure", "backup failed");
        Assert.True(failure.Applied);
        var late = await Submit(client, setup.ReportingToken, Guid.NewGuid(), Noon.AddMinutes(-2), "success");
        Assert.False(late.Applied);
        Assert.Equal(3, late.Sequence);

        await using var context = AnInstance.ContextFor(setup.ConnectionString);
        Assert.Equal(3, await context.PushReports.CountAsync(TestContext.Current.CancellationToken));
        var monitor = await context.PushMonitors.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Noon.AddMinutes(-1), monitor.LastAppliedObservedAt);
        Assert.Equal(2, monitor.LastAppliedSequence);
        Assert.Equal(late.ReceivedAt, monitor.LastReceivedAt);
    }

    [Fact]
    public async Task Invalid_rotated_and_revoked_credentials_share_the_rejection_boundary()
    {
        var clock = new MutableTimeProvider(Noon);
        var setup = await EstablishedAsync(clock);
        await using var instance = setup.Instance;
        using var client = setup.Client;
        using (var unknown = await JsonAsync(client, HttpMethod.Post, "/api/reports",
            new SubmitPushReportRequest(Guid.NewGuid(), Noon, "success", null), "uar_" + new string('a', 43)))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
            Assert.Equal("reporting_rejected", (await unknown.Content.ReadFromJsonAsync<ProblemResponse>(Json, TestContext.Current.CancellationToken))!.Code);
        }

        using var rotation = await Send(client, HttpMethod.Post,
            "/api/projects/backups/push-monitors/nightly-backup/reporting-credential/rotate", setup.ManagementToken);
        var rotated = JsonDocument.Parse(await rotation.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .RootElement.GetProperty("token").GetString()!;
        Assert.True((await Submit(client, setup.ReportingToken, Guid.NewGuid(), Noon, "success")).Applied);
        Assert.True((await Submit(client, rotated, Guid.NewGuid(), Noon.AddSeconds(1), "success")).Applied);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Delete,
            "/api/projects/backups/push-monitors/nightly-backup/reporting-credential", setup.ManagementToken)).StatusCode);
        foreach (var rejected in new[] { setup.ReportingToken, rotated })
        {
            using var response = await JsonAsync(client, HttpMethod.Post, "/api/reports",
                new SubmitPushReportRequest(Guid.NewGuid(), Noon.AddSeconds(2), "success", null), rejected);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Concurrent_retries_persist_one_report()
    {
        var setup = await EstablishedAsync(new MutableTimeProvider(Noon));
        await using var instance = setup.Instance;
        using var client = setup.Client;
        var id = Guid.NewGuid();
        var body = new SubmitPushReportRequest(id, Noon, "success", null);
        var responses = await Task.WhenAll(
            JsonAsync(client, HttpMethod.Post, "/api/reports", body, setup.ReportingToken),
            JsonAsync(client, HttpMethod.Post, "/api/reports", body, setup.ReportingToken));
        Assert.All(responses, value => Assert.Equal(HttpStatusCode.Accepted, value.StatusCode));
        var receipts = await Task.WhenAll(responses.Select(value => value.Content.ReadFromJsonAsync<PushReportReceiptResponse>(Json, TestContext.Current.CancellationToken)));
        Assert.Equal(1, receipts.Count(value => value!.Duplicate));
        await using var context = AnInstance.ContextFor(setup.ConnectionString);
        Assert.Equal(1, await context.PushReports.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Simple_secret_route_accepts_get_and_post_and_never_logs_the_secret()
    {
        var clock = new MutableTimeProvider(Noon);
        var logs = new CollectingLoggerProvider();
        var setup = await EstablishedAsync(clock, logs);
        await using var instance = setup.Instance;
        using var client = setup.Client;

        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync(
            $"/api/report/{setup.ReportingToken}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Post,
            $"/api/report/{setup.ReportingToken}")).StatusCode);

        using var rotation = await Send(client, HttpMethod.Post,
            "/api/projects/backups/push-monitors/nightly-backup/reporting-credential/rotate", setup.ManagementToken);
        var replacement = JsonDocument.Parse(await rotation.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .RootElement.GetProperty("token").GetString()!;
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync(
            $"/api/report/{setup.ReportingToken}", TestContext.Current.CancellationToken)).StatusCode);
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(
            $"/api/report/{setup.ReportingToken}", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync(
            $"/api/report/{replacement}", TestContext.Current.CancellationToken)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Delete,
            "/api/projects/backups/push-monitors/nightly-backup/reporting-credential", setup.ManagementToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(
            $"/api/report/{replacement}", TestContext.Current.CancellationToken)).StatusCode);

        Assert.DoesNotContain(logs.Messages, message =>
            message.Contains(setup.ReportingToken, StringComparison.Ordinal)
            || message.Contains(replacement, StringComparison.Ordinal));
        Assert.Contains(logs.Messages, message =>
            message.Contains("/api/report/{redacted}", StringComparison.Ordinal));

        await using var context = AnInstance.ContextFor(setup.ConnectionString);
        Assert.Equal(4, await context.PushReports.CountAsync(TestContext.Current.CancellationToken));
        Assert.All(await context.PushReports.ToListAsync(TestContext.Current.CancellationToken), report =>
        {
            Assert.Equal(Domain.Monitoring.ReportOutcome.Success, report.Outcome);
            Assert.False(report.IsDeadlineObservation);
        });
    }

    private async Task<Setup> EstablishedAsync(MutableTimeProvider clock, CollectingLoggerProvider? logs = null)
    {
        var connection = await postgres.CreateDatabaseAsync();
        var instance = AnInstance.Against(connection, new Dictionary<string, string?> { [BootstrapSettings.Variable] = Proof }, clock, logs);
        var setupClient = instance.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await setupClient.PostAsJsonAsync("/api/bootstrap",
            new BootstrapRequest(Proof, "operator@example.test", "a long operator password"), TestContext.Current.CancellationToken)).StatusCode);
        setupClient.Dispose();
        var client = instance.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var signIn = await client.PostAsJsonAsync("/api/session", new SignInRequest("operator@example.test", "a long operator password"), TestContext.Current.CancellationToken);
        var browser = Assert.Single(signIn.Headers.GetValues("Set-Cookie")).Split(';', 2)[0].Split('=', 2)[1];
        using var managementResponse = await JsonAsync(client, HttpMethod.Post, "/api/management-credentials",
            new CreateCredentialRequest("report test agent"), cookie: browser, csrf: true);
        var management = (await managementResponse.Content.ReadFromJsonAsync<IssuedCredentialResponse>(Json, TestContext.Current.CancellationToken))!.Token;
        Assert.Equal(HttpStatusCode.Created, (await JsonAsync(client, HttpMethod.Post, "/api/projects",
            new CreateProjectRequest("backups", "Backups"), management)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await JsonAsync(client, HttpMethod.Post, "/api/projects/backups/push-monitors",
            new CreatePushMonitorRequest("nightly-backup", "Nightly backup", "job_completion", 86400, 3600, null, null), management)).StatusCode);
        using var reportingResponse = await Send(client, HttpMethod.Post,
            "/api/projects/backups/push-monitors/nightly-backup/reporting-credential", management);
        var reporting = JsonDocument.Parse(await reportingResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .RootElement.GetProperty("token").GetString()!;
        return new(instance, client, connection, management, reporting);
    }

    private static async Task<PushReportReceiptResponse> Submit(HttpClient client, string token, Guid id, DateTimeOffset observed, string outcome, string? reason = null)
    {
        using var response = await JsonAsync(client, HttpMethod.Post, "/api/reports", new SubmitPushReportRequest(id, observed, outcome, reason), token);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PushReportReceiptResponse>(Json, TestContext.Current.CancellationToken))!;
    }

    private static Task<HttpResponseMessage> JsonAsync<T>(HttpClient client, HttpMethod method, string path, T body, string? bearer = null, string? cookie = null, bool csrf = false)
    {
        var request = Request(method, path, bearer, cookie, csrf);
        request.Content = JsonContent.Create(body, options: Json);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string? bearer = null) =>
        client.SendAsync(Request(method, path, bearer, null, false), TestContext.Current.CancellationToken);

    private static HttpRequestMessage Request(HttpMethod method, string path, string? bearer, string? cookie, bool csrf)
    {
        var request = new HttpRequestMessage(method, path);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (cookie is not null) request.Headers.Add("Cookie", BrowserCookie.PlainName + "=" + cookie);
        if (csrf) { request.Headers.Add(CsrfProtection.Header, "1"); request.Headers.Add("Origin", "http://localhost"); }
        return request;
    }

    private sealed record Setup(AnInstance Instance, HttpClient Client, string ConnectionString, string ManagementToken, string ReportingToken);
}
