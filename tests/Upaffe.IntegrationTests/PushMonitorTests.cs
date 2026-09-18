using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Upaffe.Api.Http;
using Upaffe.Application.Access;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class PushMonitorTests(PostgresFixture postgres)
{
    private const string Proof = "a-push-monitor-bootstrap-proof-with-enough-entropy";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Fact]
    public async Task Authenticated_push_monitor_lifecycle_is_idempotent_versioned_and_secret_free()
    {
        await using var instance = AnInstance.Against(await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?> { [BootstrapSettings.Variable] = Proof });
        using var setup = instance.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await setup.PostAsJsonAsync("/api/bootstrap",
            new BootstrapRequest(Proof, "operator@example.test", "a long operator password"), TestContext.Current.CancellationToken)).StatusCode);
        using var client = instance.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var browserResponse = await client.PostAsJsonAsync("/api/session", new SignInRequest("operator@example.test", "a long operator password"), TestContext.Current.CancellationToken);
        var browser = Assert.Single(browserResponse.Headers.GetValues("Set-Cookie")).Split(';', 2)[0].Split('=', 2)[1];
        var issued = await JsonAsync(client, HttpMethod.Post, "/api/management-credentials", new CreateCredentialRequest("push test agent"), cookie: browser, csrf: true);
        var token = (await issued.Content.ReadFromJsonAsync<IssuedCredentialResponse>(Json, TestContext.Current.CancellationToken))!.Token;
        Assert.Equal(HttpStatusCode.Created, (await JsonAsync(client, HttpMethod.Post, "/api/projects", new CreateProjectRequest("backups", "Backups"), token)).StatusCode);

        var create = new CreatePushMonitorRequest("nightly-backup", "Nightly backup", "job_completion", 86400, 3600,
            "Inspect the backup logs.", "https://runbooks.example.test/nightly-backup");
        var createdResponse = await JsonAsync(client, HttpMethod.Post, "/api/projects/backups/push-monitors", create, token);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var body = await createdResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("uar_", body, StringComparison.Ordinal);
        var created = JsonSerializer.Deserialize<PushMonitorResponse>(body, Json)!;
        Assert.Equal("untested", created.State);
        Assert.Equal("job_completion", created.Mode);
        Assert.False(created.HasReportingCredential);
        Assert.NotNull(created.NextDeadlineAt);

        Assert.Equal(HttpStatusCode.OK, (await JsonAsync(client, HttpMethod.Post, "/api/projects/backups/push-monitors", create, token)).StatusCode);
        var paused = await Read(await JsonAsync(client, HttpMethod.Post, "/api/projects/backups/push-monitors/nightly-backup/pause", new PushMonitorVersionRequest(created.Version), token));
        Assert.Equal("paused", paused.State);
        Assert.Null(paused.NextDeadlineAt);
        var resumed = await Read(await JsonAsync(client, HttpMethod.Post, "/api/projects/backups/push-monitors/nightly-backup/resume", new PushMonitorVersionRequest(paused.Version), token));
        Assert.Equal("untested", resumed.State);
        Assert.NotNull(resumed.NextDeadlineAt);

        var updated = await Read(await JsonAsync(client, HttpMethod.Put, "/api/projects/backups/push-monitors/nightly-backup",
            new UpdatePushMonitorRequest("Renamed backup", 43200, 1800, null, null, resumed.Version), token));
        Assert.Equal("Renamed backup", updated.Name);
        using var stale = await JsonAsync(client, HttpMethod.Put, "/api/projects/backups/push-monitors/nightly-backup",
            new UpdatePushMonitorRequest("Stale", 43200, 1800, null, null, resumed.Version), token);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var removed = await Send(client, HttpMethod.Delete, $"/api/projects/backups/push-monitors/nightly-backup?version={updated.Version}", token);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using var missing = await Send(client, HttpMethod.Get, "/api/projects/backups/push-monitors/nightly-backup", token);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var anonymous = await Send(client, HttpMethod.Get, "/api/projects/backups/push-monitors");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private static async Task<PushMonitorResponse> Read(HttpResponseMessage response)
    {
        using (response) return (await response.Content.ReadFromJsonAsync<PushMonitorResponse>(Json, TestContext.Current.CancellationToken))!;
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
}
