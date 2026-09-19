using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Upaffe.Api.Http;
using Upaffe.Application.Access;
using Upaffe.Application.Ports;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class HttpMonitorTests(PostgresFixture postgres)
{
    private const string BootstrapProof = "an-http-monitor-test-bootstrap-proof-with-enough-entropy";
    private const string Email = "operator@example.test";
    private const string Password = "a long operator password";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public async Task An_HTTP_monitor_has_a_complete_secret_safe_management_lifecycle()
    {
        var executor = new StubExecutor(new HttpExecutionResult(
            true,
            null,
            "The HTTP check succeeded.",
            200,
            42,
            "https://status.example.test/health"));
        await using var instance = await EstablishedAsync(executor);
        using var client = Client(instance);
        var browser = await SignInAsync(client);
        var token = await CredentialAsync(client, browser);
        await CreateProjectAsync(client, token, "public-services");

        const string targetSecret = "query-value-never-returned";
        const string headerSecret = "header-value-never-returned";
        var createRequest = new CreateHttpMonitorRequest(
            "public-site",
            "Public site",
            $"https://status.example.test/health?token={targetSecret}",
            200,
            "required",
            "ready",
            300,
            10,
            3,
            "Inspect the current deployment.",
            "https://runbooks.example.test/public-site",
            [new("Authorization", $"Bearer {headerSecret}")]);
        using var createdResponse = await JsonAsync(
            client, HttpMethod.Post, "/api/projects/public-services/http-monitors", createRequest, token);
        var createdJson = await createdResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(
            createdResponse.StatusCode == HttpStatusCode.Created,
            $"Expected monitor creation, received {createdResponse.StatusCode}: {createdJson}");
        Assert.DoesNotContain(targetSecret, createdJson, StringComparison.Ordinal);
        Assert.DoesNotContain(headerSecret, createdJson, StringComparison.Ordinal);
        var created = JsonSerializer.Deserialize<HttpMonitorResponse>(createdJson, Json)!;
        Assert.Equal("untested", created.State);
        Assert.Equal("https://status.example.test/health", created.TargetUrl);
        Assert.True(created.HasTargetQuery);
        Assert.Equal("authorization", Assert.Single(created.Headers).Name);

        using (var repeated = await JsonAsync(
            client, HttpMethod.Post, "/api/projects/public-services/http-monitors", createRequest, token))
        {
            Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
            Assert.Equal(created.Id, (await MonitorAsync(repeated)).Id);
        }

        var listed = await MonitorsAsync(await SendAsync(
            client, HttpMethod.Get, "/api/projects/public-services/http-monitors", token));
        Assert.Equal(created.Id, Assert.Single(listed).Id);

        var updated = await MonitorAsync(await JsonAsync(
            client,
            HttpMethod.Put,
            "/api/projects/public-services/http-monitors/public-site",
            new UpdateHttpMonitorRequest(
                "Renamed public site",
                null,
                200,
                "required",
                "ready",
                600,
                15,
                2,
                null,
                "https://runbooks.example.test/public-site",
                created.Version),
            token));
        Assert.Equal("Renamed public site", updated.Name);
        Assert.True(updated.HasTargetQuery);

        const string replacementSecret = "replacement-never-returned";
        using var headerResponse = await JsonAsync(
            client,
            HttpMethod.Put,
            "/api/projects/public-services/http-monitors/public-site/headers/authorization",
            new SetHttpMonitorHeaderRequest($"Bearer {replacementSecret}", updated.Version),
            token);
        var headerJson = await headerResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(replacementSecret, headerJson, StringComparison.Ordinal);
        var withHeader = JsonSerializer.Deserialize<HttpMonitorResponse>(headerJson, Json)!;

        using var testedResponse = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/projects/public-services/http-monitors/public-site/test",
            token);
        var tested = (await testedResponse.Content.ReadFromJsonAsync<HttpMonitorTestResponse>(
            Json,
            TestContext.Current.CancellationToken))!;
        Assert.True(tested.Succeeded);
        Assert.True(tested.AppliedToCurrentState);
        Assert.Equal("healthy", tested.Monitor.State);
        Assert.Equal(tested.CheckId, tested.Monitor.LatestResultId);
        Assert.Equal(tested.CheckId, tested.Monitor.LatestSuccessId);
        var execution = Assert.Single(executor.Requests);
        Assert.Contains(targetSecret, execution.TargetUrl, StringComparison.Ordinal);
        Assert.Equal($"Bearer {replacementSecret}", Assert.Single(execution.Headers).Value);

        var paused = await MonitorAsync(await JsonAsync(
            client,
            HttpMethod.Post,
            "/api/projects/public-services/http-monitors/public-site/pause",
            new HttpMonitorVersionRequest(withHeader.Version),
            token));
        Assert.Equal("paused", paused.State);
        Assert.Null(paused.NextCheckAt);

        using (var pausedTest = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/projects/public-services/http-monitors/public-site/test",
            token))
        {
            Assert.Equal(HttpStatusCode.Conflict, pausedTest.StatusCode);
            Assert.Equal("conflict", await ProblemCode(pausedTest));
        }

        var resumed = await MonitorAsync(await JsonAsync(
            client,
            HttpMethod.Post,
            "/api/projects/public-services/http-monitors/public-site/resume",
            new HttpMonitorVersionRequest(paused.Version),
            token));
        Assert.Equal("untested", resumed.State);
        Assert.NotNull(resumed.NextCheckAt);
        Assert.Equal(tested.CheckId, resumed.LatestSuccessId);

        var removed = await MonitorAsync(await SendAsync(
            client,
            HttpMethod.Delete,
            $"/api/projects/public-services/http-monitors/public-site?version={resumed.Version}",
            token));
        Assert.NotNull(removed.DeletedAt);
        Assert.Empty(await MonitorsAsync(await SendAsync(
            client, HttpMethod.Get, "/api/projects/public-services/http-monitors", token)));
        using var missing = await SendAsync(
            client, HttpMethod.Get, "/api/projects/public-services/http-monitors/public-site", token);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Invalid_project_configuration_and_concurrency_have_stable_contracts()
    {
        await using var instance = await EstablishedAsync(new StubExecutor());
        using var client = Client(instance);
        var browser = await SignInAsync(client);
        var token = await CredentialAsync(client, browser);

        using (var missingProject = await JsonAsync(
            client,
            HttpMethod.Post,
            "/api/projects/missing/http-monitors",
            ValidCreate(),
            token))
        {
            var body = await missingProject.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(
                missingProject.StatusCode == HttpStatusCode.NotFound,
                $"Expected missing project, received {missingProject.StatusCode}: {body}");
        }

        await CreateProjectAsync(client, token, "one-project");
        await CreateProjectAsync(client, token, "other-project");
        using (var invalid = await JsonAsync(
            client,
            HttpMethod.Post,
            "/api/projects/one-project/http-monitors",
            ValidCreate() with { TextCondition = "none", TextFragment = "contradiction" },
            token))
        {
            var problem = await Problem(invalid);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("validation", problem?.Code);
        }

        var created = await MonitorAsync(await JsonAsync(
            client,
            HttpMethod.Post,
            "/api/projects/one-project/http-monitors",
            ValidCreate(),
            token));
        using (var wrongProject = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/projects/other-project/http-monitors/public-site",
            token))
        {
            Assert.Equal(HttpStatusCode.NotFound, wrongProject.StatusCode);
        }

        var update = new UpdateHttpMonitorRequest(
            "First update", null, 200, "none", null, 300, 10, 3, null, null, created.Version);
        using (var first = await JsonAsync(
            client,
            HttpMethod.Put,
            "/api/projects/one-project/http-monitors/public-site",
            update,
            token))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using (var stale = await JsonAsync(
            client,
            HttpMethod.Put,
            "/api/projects/one-project/http-monitors/public-site",
            update with { Name = "Stale update" },
            token))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("conflict", await ProblemCode(stale));
        }

        using var forbiddenHeader = await JsonAsync(
            client,
            HttpMethod.Put,
            "/api/projects/one-project/http-monitors/public-site/headers/host",
            new SetHttpMonitorHeaderRequest("private.example", created.Version + 1),
            token);
        Assert.Equal(HttpStatusCode.BadRequest, forbiddenHeader.StatusCode);
        Assert.Equal("validation", await ProblemCode(forbiddenHeader));
    }

    [Fact]
    public async Task Monitor_responses_distinguish_subthreshold_incident_pause_resume_and_recovery()
    {
        var executor = new StubExecutor(
            new(false, "timeout", "The check timed out.", null, 10_000, null),
            new(false, "unexpected_status", "The status differed.", 503, 20, "https://status.example.test/health"),
            new(false, "text_missing", "The text was absent.", 200, 30, "https://status.example.test/health"),
            new(true, null, "The check succeeded.", 200, 15, "https://status.example.test/health"));
        await using var instance = await EstablishedAsync(executor);
        using var client = Client(instance);
        var browser = await SignInAsync(client);
        var token = await CredentialAsync(client, browser);
        await CreateProjectAsync(client, token, "public-services");

        var created = await MonitorAsync(await JsonAsync(
            client,
            HttpMethod.Post,
            "/api/projects/public-services/http-monitors",
            ValidCreate() with { FailureThreshold = 2 },
            token));
        Assert.Equal("untested", created.State);
        Assert.Null(created.OpenIncidentId);

        var first = (await TestAsync(client, token)).Monitor;
        Assert.Equal("failing", first.State);
        Assert.Equal(1, first.ConsecutiveFailures);
        Assert.Null(first.OpenIncidentId);

        var opening = (await TestAsync(client, token)).Monitor;
        Assert.Equal("failing", opening.State);
        Assert.Equal(2, opening.ConsecutiveFailures);
        Assert.NotNull(opening.OpenIncidentId);

        var paused = await MonitorAsync(await JsonAsync(
            client,
            HttpMethod.Post,
            "/api/projects/public-services/http-monitors/public-site/pause",
            new HttpMonitorVersionRequest(opening.Version),
            token));
        Assert.Equal("paused", paused.State);
        Assert.Null(paused.NextCheckAt);
        Assert.Equal(opening.OpenIncidentId, paused.OpenIncidentId);

        var resumed = await MonitorAsync(await JsonAsync(
            client,
            HttpMethod.Post,
            "/api/projects/public-services/http-monitors/public-site/resume",
            new HttpMonitorVersionRequest(paused.Version),
            token));
        Assert.Equal("untested", resumed.State);
        Assert.Equal(0, resumed.ConsecutiveFailures);
        Assert.NotNull(resumed.NextCheckAt);
        Assert.Equal(opening.OpenIncidentId, resumed.OpenIncidentId);

        var freshFailure = (await TestAsync(client, token)).Monitor;
        Assert.Equal("failing", freshFailure.State);
        Assert.Equal(1, freshFailure.ConsecutiveFailures);
        Assert.Equal(opening.OpenIncidentId, freshFailure.OpenIncidentId);

        var recovered = (await TestAsync(client, token)).Monitor;
        Assert.Equal("healthy", recovered.State);
        Assert.Equal(0, recovered.ConsecutiveFailures);
        Assert.Null(recovered.OpenIncidentId);
    }

    [Fact]
    public async Task Check_and_incident_history_are_cursor_paginated_and_secret_safe()
    {
        const string querySecret = "history-query-never-returned";
        const string headerSecret = "history-header-never-returned";
        const string responseSecret = "history-response-query-never-returned";
        var executor = new StubExecutor(
            new(false, "timeout", "The check timed out.", null, 10_000, null),
            new(true, null, "The check succeeded.", 200, 12, $"https://status.example.test/health?proof={responseSecret}"),
            new(false, "unexpected_status", "The status differed.", 503, 25, $"https://status.example.test/health?proof={responseSecret}"),
            new(true, null, "The check succeeded.", 200, 13, $"https://status.example.test/health?proof={responseSecret}"),
            new(false, "text_missing", "The text was absent.", 200, 30, $"https://status.example.test/health?proof={responseSecret}"),
            new(true, null, "The check succeeded.", 200, 15, $"https://status.example.test/health?proof={responseSecret}"));
        await using var instance = await EstablishedAsync(executor);
        using var client = Client(instance);
        var browser = await SignInAsync(client);
        var token = await CredentialAsync(client, browser);
        await CreateProjectAsync(client, token, "public-services");
        var request = ValidCreate() with
        {
            TargetUrl = $"https://status.example.test/health?token={querySecret}",
            FailureThreshold = 1,
            Headers = [new("Authorization", $"Bearer {headerSecret}")],
        };
        _ = await MonitorAsync(await JsonAsync(
            client, HttpMethod.Post, "/api/projects/public-services/http-monitors", request, token));
        _ = await TestAsync(client, token);
        _ = await TestAsync(client, token);
        _ = await TestAsync(client, token);
        _ = await TestAsync(client, token);
        _ = await TestAsync(client, token);
        _ = await TestAsync(client, token);

        using var firstResponse = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/projects/public-services/http-monitors/public-site/checks?limit=2",
            token);
        var firstBody = await firstResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(querySecret, firstBody, StringComparison.Ordinal);
        Assert.DoesNotContain(headerSecret, firstBody, StringComparison.Ordinal);
        Assert.DoesNotContain(responseSecret, firstBody, StringComparison.Ordinal);
        var first = JsonSerializer.Deserialize<HttpCheckHistoryPageResponse>(firstBody, Json)!;
        Assert.Equal(new long[] { 6, 5 }, first.Items.Select(value => value.Sequence));
        Assert.Equal(5, first.NextBeforeSequence);
        Assert.Equal("success", first.Items[0].Outcome);
        Assert.Equal(200, first.Items[0].StatusCode);
        Assert.Equal(15, first.Items[0].ResponseTimeMilliseconds);
        Assert.Equal("failure", first.Items[1].Outcome);
        Assert.Equal("text_missing", first.Items[1].FailureReason);

        using var secondResponse = await SendAsync(
            client,
            HttpMethod.Get,
            $"/api/projects/public-services/http-monitors/public-site/checks?limit=2&before_sequence={first.NextBeforeSequence}",
            token);
        var second = (await secondResponse.Content.ReadFromJsonAsync<HttpCheckHistoryPageResponse>(
            Json,
            TestContext.Current.CancellationToken))!;
        Assert.Equal(new long[] { 4, 3 }, second.Items.Select(value => value.Sequence));
        Assert.Equal(3, second.NextBeforeSequence);

        using var thirdResponse = await SendAsync(
            client,
            HttpMethod.Get,
            $"/api/projects/public-services/http-monitors/public-site/checks?limit=2&before_sequence={second.NextBeforeSequence}",
            token);
        var third = (await thirdResponse.Content.ReadFromJsonAsync<HttpCheckHistoryPageResponse>(
            Json,
            TestContext.Current.CancellationToken))!;
        Assert.Equal(new long[] { 2, 1 }, third.Items.Select(value => value.Sequence));
        Assert.Null(third.NextBeforeSequence);

        using var evidenceResponse = await SendAsync(client, HttpMethod.Get,
            $"/api/projects/public-services/http-monitors/public-site/checks/{third.Items[1].Id}", token);
        var evidenceBody = await evidenceResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, evidenceResponse.StatusCode);
        Assert.DoesNotContain(querySecret, evidenceBody, StringComparison.Ordinal);
        Assert.DoesNotContain(headerSecret, evidenceBody, StringComparison.Ordinal);
        Assert.DoesNotContain(responseSecret, evidenceBody, StringComparison.Ordinal);
        var evidence = JsonSerializer.Deserialize<HttpCheckHistoryResponse>(evidenceBody, Json)!;
        Assert.Equal(1, evidence.Sequence);
        using var otherMonitor = await SendAsync(client, HttpMethod.Get,
            $"/api/projects/public-services/http-monitors/other/checks/{third.Items[1].Id}", token);
        Assert.Equal(HttpStatusCode.NotFound, otherMonitor.StatusCode);
        using var anonymousEvidence = await SendAsync(client, HttpMethod.Get,
            $"/api/projects/public-services/http-monitors/public-site/checks/{third.Items[1].Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousEvidence.StatusCode);

        using var incidentsResponse = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/projects/public-services/http-monitors/public-site/incidents?limit=2",
            token);
        var incidents = (await incidentsResponse.Content.ReadFromJsonAsync<IncidentHistoryPageResponse>(
            Json,
            TestContext.Current.CancellationToken))!;
        Assert.Equal(new long[] { 5, 3 }, incidents.Items.Select(value => value.OpeningSequence));
        Assert.Equal(3, incidents.NextBeforeOpeningSequence);
        Assert.All(incidents.Items, value => Assert.NotNull(value.ResolvedAt));

        using var olderIncidentsResponse = await SendAsync(
            client,
            HttpMethod.Get,
            $"/api/projects/public-services/http-monitors/public-site/incidents?limit=2&before_opening_sequence={incidents.NextBeforeOpeningSequence}",
            token);
        var olderIncidents = (await olderIncidentsResponse.Content.ReadFromJsonAsync<IncidentHistoryPageResponse>(
            Json,
            TestContext.Current.CancellationToken))!;
        var oldestIncident = Assert.Single(olderIncidents.Items);
        Assert.Equal(1, oldestIncident.FirstFailureSequence);
        Assert.Equal(1, oldestIncident.OpeningSequence);
        Assert.Equal(2, oldestIncident.ResolutionSequence);
        Assert.Equal("timeout", oldestIncident.OriginalReason);
        Assert.Equal("timeout", oldestIncident.LatestReason);
        Assert.NotNull(oldestIncident.ResolvedAt);
        Assert.Null(olderIncidents.NextBeforeOpeningSequence);

        using var incidentEvidenceResponse = await SendAsync(client, HttpMethod.Get,
            $"/api/projects/public-services/http-monitors/public-site/incidents/{oldestIncident.Id}", token);
        Assert.Equal(HttpStatusCode.OK, incidentEvidenceResponse.StatusCode);
        var incidentEvidence = (await incidentEvidenceResponse.Content.ReadFromJsonAsync<IncidentHistoryResponse>(
            Json, TestContext.Current.CancellationToken))!;
        Assert.Equal(1, incidentEvidence.OpeningSequence);

        using var invalidLimit = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/projects/public-services/http-monitors/public-site/checks?limit=101",
            token);
        Assert.Equal(HttpStatusCode.BadRequest, invalidLimit.StatusCode);
        Assert.Equal("validation", await ProblemCode(invalidLimit));
    }

    private async Task<AnInstance> EstablishedAsync(IHttpCheckExecutor executor)
    {
        var instance = AnInstance.Against(
            await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?> { [BootstrapSettings.Variable] = BootstrapProof },
            checkExecutor: executor);
        using var client = instance.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/bootstrap",
            new BootstrapRequest(BootstrapProof, Email, Password),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return instance;
    }

    private static CreateHttpMonitorRequest ValidCreate() => new(
        "public-site",
        "Public site",
        "https://status.example.test/health",
        200,
        "none",
        null,
        300,
        10,
        3,
        null,
        null,
        []);

    private static async Task CreateProjectAsync(HttpClient client, string token, string key)
    {
        using var response = await JsonAsync(
            client, HttpMethod.Post, "/api/projects", new CreateProjectRequest(key, key), token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<HttpMonitorTestResponse> TestAsync(HttpClient client, string token)
    {
        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/projects/public-services/http-monitors/public-site/test",
            token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<HttpMonitorTestResponse>(
            Json,
            TestContext.Current.CancellationToken))!;
    }

    private static HttpClient Client(AnInstance instance) =>
        instance.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static async Task<string> SignInAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/session",
            new SignInRequest(Email, Password),
            TestContext.Current.CancellationToken);
        return Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';', 2)[0].Split('=', 2)[1];
    }

    private static async Task<string> CredentialAsync(HttpClient client, string browser)
    {
        using var response = await JsonAsync(
            client,
            HttpMethod.Post,
            "/api/management-credentials",
            new CreateCredentialRequest("monitor test agent"),
            cookie: browser,
            csrf: true);
        return (await response.Content.ReadFromJsonAsync<IssuedCredentialResponse>(
            Json,
            TestContext.Current.CancellationToken))!.Token;
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? bearer = null,
        string? cookie = null,
        bool csrf = false) =>
        client.SendAsync(Request(method, path, bearer, cookie, csrf), TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> JsonAsync<T>(
        HttpClient client,
        HttpMethod method,
        string path,
        T body,
        string? bearer = null,
        string? cookie = null,
        bool csrf = false)
    {
        var request = Request(method, path, bearer, cookie, csrf);
        request.Content = JsonContent.Create(body, options: Json);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static HttpRequestMessage Request(
        HttpMethod method,
        string path,
        string? bearer,
        string? cookie,
        bool csrf)
    {
        var request = new HttpRequestMessage(method, path);
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", BrowserCookie.PlainName + "=" + cookie);
        }

        if (csrf)
        {
            request.Headers.Add(CsrfProtection.Header, "1");
            request.Headers.Add("Origin", "http://localhost");
        }

        return request;
    }

    private static async Task<HttpMonitorResponse> MonitorAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var monitor = await response.Content.ReadFromJsonAsync<HttpMonitorResponse>(
                Json,
                TestContext.Current.CancellationToken);
            Assert.NotNull(monitor);
            return monitor;
        }
    }

    private static async Task<HttpMonitorResponse[]> MonitorsAsync(HttpResponseMessage response)
    {
        using (response)
        {
            return (await response.Content.ReadFromJsonAsync<HttpMonitorResponse[]>(
                Json,
                TestContext.Current.CancellationToken))!;
        }
    }

    private static async Task<string?> ProblemCode(HttpResponseMessage response) =>
        (await Problem(response))?.Code;

    private static Task<ProblemResponse?> Problem(HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<ProblemResponse>(TestContext.Current.CancellationToken);

    private sealed class StubExecutor(params HttpExecutionResult[] results) : IHttpCheckExecutor
    {
        private readonly Queue<HttpExecutionResult> results = new(results);

        public List<HttpExecutionRequest> Requests { get; } = [];

        public Task<HttpExecutionResult> ExecuteAsync(
            HttpExecutionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (results.Count == 0)
            {
                throw new InvalidOperationException("No stub HTTP result was configured.");
            }

            return Task.FromResult(results.Dequeue());
        }
    }
}
