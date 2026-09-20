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
        var recovery = await Submit(client, setup.ReportingToken, Guid.NewGuid(), Noon.AddSeconds(1), "success");
        Assert.True(recovery.Applied);
        Assert.Equal(4, recovery.Sequence);

        await using var context = AnInstance.ContextFor(setup.ConnectionString);
        Assert.Equal(4, await context.PushReports.CountAsync(TestContext.Current.CancellationToken));
        var monitor = await context.PushMonitors.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Domain.Monitoring.MonitorState.Healthy, monitor.State);
        Assert.Equal(Noon.AddSeconds(1), monitor.LastAppliedObservedAt);
        Assert.Equal(4, monitor.LastAppliedSequence);
        Assert.Equal(recovery.ReceivedAt, monitor.LastReceivedAt);
        Assert.Equal(Noon.AddHours(25), monitor.NextDeadlineAt);
        Assert.NotNull(monitor.LatestSuccessId);
        var incident = await context.PushIncidents.SingleAsync(TestContext.Current.CancellationToken);
        Assert.False(incident.IsOpen);
        Assert.Equal(2, incident.OpeningSequence);
        Assert.Equal(4, incident.ResolutionSequence);
        Assert.Equal("reported_failure", incident.OriginalReason);
    }

    [Fact]
    public async Task State_reports_refresh_freshness_on_failure_and_reuse_one_incident()
    {
        var clock = new MutableTimeProvider(Noon);
        var setup = await EstablishedAsync(clock);
        await using var instance = setup.Instance;
        using var client = setup.Client;
        Assert.Equal(HttpStatusCode.Created, (await JsonAsync(client, HttpMethod.Post, "/api/projects/backups/push-monitors",
            new CreatePushMonitorRequest("local-health", "Local health", "state_report", 60, 30, null, null), setup.ManagementToken)).StatusCode);
        using var issued = await Send(client, HttpMethod.Post,
            "/api/projects/backups/push-monitors/local-health/reporting-credential", setup.ManagementToken);
        var token = JsonDocument.Parse(await issued.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .RootElement.GetProperty("token").GetString()!;

        await Submit(client, token, Guid.NewGuid(), Noon.AddSeconds(-1), "success");
        clock.Advance(TimeSpan.FromSeconds(10));
        await Submit(client, token, Guid.NewGuid(), Noon.AddSeconds(5), "failure", "condition failed");
        await using (var afterFailure = AnInstance.ContextFor(setup.ConnectionString))
        {
            var monitor = await afterFailure.PushMonitors.SingleAsync(value => value.Key == "local-health", TestContext.Current.CancellationToken);
            Assert.Equal(Domain.Monitoring.MonitorState.Failing, monitor.State);
            Assert.Equal(Noon.AddSeconds(100), monitor.NextDeadlineAt);
            Assert.Equal(Noon.AddSeconds(10), monitor.LastReceivedAt);
            Assert.NotNull(monitor.LatestSuccessId);
            Assert.True((await afterFailure.PushIncidents.SingleAsync(TestContext.Current.CancellationToken)).IsOpen);
        }

        clock.Advance(TimeSpan.FromSeconds(10));
        await Submit(client, token, Guid.NewGuid(), Noon.AddSeconds(15), "failure", "still failed");
        clock.Advance(TimeSpan.FromSeconds(10));
        await Submit(client, token, Guid.NewGuid(), Noon.AddSeconds(25), "success");
        await using var recovered = AnInstance.ContextFor(setup.ConnectionString);
        var stored = await recovered.PushMonitors.SingleAsync(value => value.Key == "local-health", TestContext.Current.CancellationToken);
        Assert.Equal(Domain.Monitoring.MonitorState.Healthy, stored.State);
        Assert.Equal(Noon.AddSeconds(120), stored.NextDeadlineAt);
        var incidents = await recovered.PushIncidents.ToListAsync(TestContext.Current.CancellationToken);
        var incident = Assert.Single(incidents);
        Assert.False(incident.IsOpen);
        Assert.Equal(2, incident.OpeningSequence);
        Assert.Equal(3, incident.LatestFailureSequence);
        Assert.Equal(4, incident.ResolutionSequence);
    }

    [Theory]
    [InlineData("job_completion")]
    [InlineData("state_report")]
    public async Task Pause_and_resume_preserve_facts_reject_reports_and_require_fresh_recovery(string mode)
    {
        var clock = new MutableTimeProvider(Noon);
        var setup = await EstablishedAsync(clock);
        await using var instance = setup.Instance;
        using var client = setup.Client;
        using var creation = await JsonAsync(client, HttpMethod.Post, "/api/projects/backups/push-monitors",
            new CreatePushMonitorRequest("lifecycle", "Lifecycle", mode, 60, 30, null, null), setup.ManagementToken);
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        var created = (await creation.Content.ReadFromJsonAsync<PushMonitorResponse>(Json, TestContext.Current.CancellationToken))!;
        Assert.Equal("untested", created.State);
        Assert.Equal(Noon.AddSeconds(90), created.NextDeadlineAt);
        Assert.Null(created.OpenIncidentId);

        using var issued = await Send(client, HttpMethod.Post,
            "/api/projects/backups/push-monitors/lifecycle/reporting-credential", setup.ManagementToken);
        var reportingToken = JsonDocument.Parse(await issued.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .RootElement.GetProperty("token").GetString()!;
        await Submit(client, reportingToken, Guid.NewGuid(), Noon.AddSeconds(-2), "success");
        var failureId = Guid.NewGuid();
        await Submit(client, reportingToken, failureId, Noon, "failure", "reported unhealthy");
        using var failingResponse = await Send(client, HttpMethod.Get,
            "/api/projects/backups/push-monitors/lifecycle", setup.ManagementToken);
        var failing = (await failingResponse.Content.ReadFromJsonAsync<PushMonitorResponse>(Json, TestContext.Current.CancellationToken))!;
        Assert.Equal("failing", failing.State);
        Assert.NotNull(failing.OpenIncidentId);
        Assert.NotNull(failing.LatestSuccessId);

        clock.Advance(TimeSpan.FromSeconds(10));
        using var pauseResponse = await JsonAsync(client, HttpMethod.Post,
            "/api/projects/backups/push-monitors/lifecycle/pause",
            new PushMonitorVersionRequest(failing.Version), setup.ManagementToken);
        var paused = (await pauseResponse.Content.ReadFromJsonAsync<PushMonitorResponse>(Json, TestContext.Current.CancellationToken))!;
        Assert.Equal("paused", paused.State);
        Assert.Null(paused.NextDeadlineAt);
        Assert.Equal(failing.OpenIncidentId, paused.OpenIncidentId);
        Assert.Equal(failing.LatestReportId, paused.LatestReportId);
        Assert.Equal(failing.LatestSuccessId, paused.LatestSuccessId);
        using (var retryWhilePaused = await JsonAsync(client, HttpMethod.Post, "/api/reports",
            new SubmitPushReportRequest(failureId, Noon, "failure", "reported unhealthy"), reportingToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, retryWhilePaused.StatusCode);
        }
        using (var successWhilePaused = await JsonAsync(client, HttpMethod.Post, "/api/reports",
            new SubmitPushReportRequest(Guid.NewGuid(), Noon.AddSeconds(1), "success", null), reportingToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, successWhilePaused.StatusCode);
        }
        using var simpleWhilePaused = await client.GetAsync(
            $"/api/report/{reportingToken}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, simpleWhilePaused.StatusCode);

        clock.Advance(TimeSpan.FromSeconds(10));
        using var resumeResponse = await JsonAsync(client, HttpMethod.Post,
            "/api/projects/backups/push-monitors/lifecycle/resume",
            new PushMonitorVersionRequest(paused.Version), setup.ManagementToken);
        var resumed = (await resumeResponse.Content.ReadFromJsonAsync<PushMonitorResponse>(Json, TestContext.Current.CancellationToken))!;
        Assert.Equal("untested", resumed.State);
        Assert.Equal(Noon.AddSeconds(110), resumed.NextDeadlineAt);
        Assert.Equal(paused.OpenIncidentId, resumed.OpenIncidentId);
        Assert.Equal(paused.LatestSuccessId, resumed.LatestSuccessId);

        var oldRetry = await Submit(client, reportingToken, failureId, Noon, "failure", "reported unhealthy");
        Assert.True(oldRetry.Duplicate);
        using var afterRetryResponse = await Send(client, HttpMethod.Get,
            "/api/projects/backups/push-monitors/lifecycle", setup.ManagementToken);
        var afterRetry = (await afterRetryResponse.Content.ReadFromJsonAsync<PushMonitorResponse>(Json, TestContext.Current.CancellationToken))!;
        Assert.Equal("untested", afterRetry.State);
        Assert.Equal(resumed.OpenIncidentId, afterRetry.OpenIncidentId);

        var recovery = await Submit(client, reportingToken, Guid.NewGuid(), Noon.AddSeconds(21), "success");
        Assert.True(recovery.Applied);
        using var recoveredResponse = await Send(client, HttpMethod.Get,
            "/api/projects/backups/push-monitors/lifecycle", setup.ManagementToken);
        var recovered = (await recoveredResponse.Content.ReadFromJsonAsync<PushMonitorResponse>(Json, TestContext.Current.CancellationToken))!;
        Assert.Equal("healthy", recovered.State);
        Assert.Null(recovered.OpenIncidentId);
        Assert.Equal(recovery.ReceivedAt, recovered.LastReceivedAt);

        await using var context = AnInstance.ContextFor(setup.ConnectionString);
        var monitor = await context.PushMonitors.SingleAsync(value => value.Id == created.Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, monitor.EvaluationGeneration);
        Assert.Equal(3, await context.PushReports.CountAsync(value => value.MonitorId == created.Id, TestContext.Current.CancellationToken));
        var incident = await context.PushIncidents.SingleAsync(value => value.MonitorId == created.Id, TestContext.Current.CancellationToken);
        Assert.False(incident.IsOpen);
        Assert.Equal(recovery.Sequence, incident.ResolutionSequence);
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
    public async Task Concurrent_distinct_failures_apply_newest_observation_and_open_one_incident()
    {
        var setup = await EstablishedAsync(new MutableTimeProvider(Noon));
        await using var instance = setup.Instance;
        using var client = setup.Client;
        var responses = await Task.WhenAll(
            JsonAsync(client, HttpMethod.Post, "/api/reports",
                new SubmitPushReportRequest(Guid.NewGuid(), Noon, "failure", "first failure"), setup.ReportingToken),
            JsonAsync(client, HttpMethod.Post, "/api/reports",
                new SubmitPushReportRequest(Guid.NewGuid(), Noon.AddSeconds(1), "failure", "newest failure"), setup.ReportingToken));
        Assert.All(responses, value => Assert.Equal(HttpStatusCode.Accepted, value.StatusCode));

        await using var context = AnInstance.ContextFor(setup.ConnectionString);
        var reports = await context.PushReports.OrderBy(value => value.Sequence)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, reports.Count);
        var newest = Assert.Single(reports, value => value.ObservedAt == Noon.AddSeconds(1));
        Assert.True(newest.Applicable);
        var monitor = await context.PushMonitors.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Domain.Monitoring.MonitorState.Failing, monitor.State);
        Assert.Equal(newest.Sequence, monitor.LastAppliedSequence);
        Assert.Equal(newest.ObservedAt, monitor.LastAppliedObservedAt);
        Assert.Equal(Noon.AddHours(25), monitor.NextDeadlineAt);
        var incident = await context.PushIncidents.SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(incident.IsOpen);
        Assert.Equal(newest.Id, incident.LatestFailureReportId);
    }

    [Fact]
    public async Task Management_history_is_newest_first_cursor_stable_and_redacts_sender_data()
    {
        var setup = await EstablishedAsync(new MutableTimeProvider(Noon));
        await using var instance = setup.Instance;
        using var client = setup.Client;
        await Submit(client, setup.ReportingToken, Guid.NewGuid(), Noon.AddSeconds(-6), "success");
        await Submit(client, setup.ReportingToken, Guid.NewGuid(), Noon.AddSeconds(-5), "failure", "private sender detail");
        await Submit(client, setup.ReportingToken, Guid.NewGuid(), Noon.AddSeconds(-4), "success");
        var late = await Submit(client, setup.ReportingToken, Guid.NewGuid(), Noon.AddSeconds(-5), "failure", "another private detail");
        Assert.False(late.Applied);
        await Submit(client, setup.ReportingToken, Guid.NewGuid(), Noon.AddSeconds(-3), "failure", "latest private detail");
        await Submit(client, setup.ReportingToken, Guid.NewGuid(), Noon.AddSeconds(-2), "success");

        using var firstResponse = await Send(client, HttpMethod.Get,
            "/api/projects/backups/push-monitors/nightly-backup/reports?limit=2", setup.ManagementToken);
        var firstBody = await firstResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(setup.ReportingToken, firstBody, StringComparison.Ordinal);
        Assert.DoesNotContain("private", firstBody, StringComparison.Ordinal);
        var first = JsonSerializer.Deserialize<PushReportHistoryPageResponse>(firstBody, Json)!;
        Assert.Equal(new long[] { 6, 5 }, first.Items.Select(value => value.Sequence));
        Assert.Equal(5, first.NextBeforeSequence);
        Assert.Equal("reported_failure", first.Items[1].Reason);

        using var secondResponse = await Send(client, HttpMethod.Get,
            $"/api/projects/backups/push-monitors/nightly-backup/reports?limit=2&before_sequence={first.NextBeforeSequence}", setup.ManagementToken);
        var second = (await secondResponse.Content.ReadFromJsonAsync<PushReportHistoryPageResponse>(Json, TestContext.Current.CancellationToken))!;
        Assert.Equal(new long[] { 4, 3 }, second.Items.Select(value => value.Sequence));
        Assert.False(second.Items[0].Applicable);
        Assert.Equal(3, second.NextBeforeSequence);

        using var thirdResponse = await Send(client, HttpMethod.Get,
            $"/api/projects/backups/push-monitors/nightly-backup/reports?limit=2&before_sequence={second.NextBeforeSequence}", setup.ManagementToken);
        var third = (await thirdResponse.Content.ReadFromJsonAsync<PushReportHistoryPageResponse>(Json, TestContext.Current.CancellationToken))!;
        Assert.Equal(new long[] { 2, 1 }, third.Items.Select(value => value.Sequence));
        Assert.Null(third.NextBeforeSequence);

        using var evidenceResponse = await Send(client, HttpMethod.Get,
            $"/api/projects/backups/push-monitors/nightly-backup/reports/{third.Items[1].Id}", setup.ManagementToken);
        var evidenceBody = await evidenceResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, evidenceResponse.StatusCode);
        Assert.DoesNotContain("private", evidenceBody, StringComparison.Ordinal);
        Assert.DoesNotContain(setup.ReportingToken, evidenceBody, StringComparison.Ordinal);
        var evidence = JsonSerializer.Deserialize<PushReportHistoryResponse>(evidenceBody, Json)!;
        Assert.Equal(1, evidence.Sequence);
        using var otherMonitor = await Send(client, HttpMethod.Get,
            $"/api/projects/backups/push-monitors/other/reports/{third.Items[1].Id}", setup.ManagementToken);
        Assert.Equal(HttpStatusCode.NotFound, otherMonitor.StatusCode);
        using var anonymousEvidence = await Send(client, HttpMethod.Get,
            $"/api/projects/backups/push-monitors/nightly-backup/reports/{third.Items[1].Id}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousEvidence.StatusCode);

        using var incidentResponse = await Send(client, HttpMethod.Get,
            "/api/projects/backups/push-monitors/nightly-backup/incidents?limit=1", setup.ManagementToken);
        var incidents = (await incidentResponse.Content.ReadFromJsonAsync<PushIncidentHistoryPageResponse>(Json, TestContext.Current.CancellationToken))!;
        var newestIncident = Assert.Single(incidents.Items);
        Assert.Equal(5, newestIncident.OpeningSequence);
        Assert.Equal(6, newestIncident.ResolutionSequence);
        Assert.Equal("reported_failure", newestIncident.OriginalReason);
        Assert.Equal(5, incidents.NextBeforeOpeningSequence);
        using var olderIncidentResponse = await Send(client, HttpMethod.Get,
            $"/api/projects/backups/push-monitors/nightly-backup/incidents?limit=1&before_opening_sequence={incidents.NextBeforeOpeningSequence}", setup.ManagementToken);
        var olderIncidents = (await olderIncidentResponse.Content.ReadFromJsonAsync<PushIncidentHistoryPageResponse>(Json, TestContext.Current.CancellationToken))!;
        Assert.Equal(2, Assert.Single(olderIncidents.Items).OpeningSequence);
        Assert.Null(olderIncidents.NextBeforeOpeningSequence);

        using var incidentEvidenceResponse = await Send(client, HttpMethod.Get,
            $"/api/projects/backups/push-monitors/nightly-backup/incidents/{olderIncidents.Items[0].Id}", setup.ManagementToken);
        Assert.Equal(HttpStatusCode.OK, incidentEvidenceResponse.StatusCode);
        var incidentEvidence = (await incidentEvidenceResponse.Content.ReadFromJsonAsync<PushIncidentHistoryResponse>(
            Json, TestContext.Current.CancellationToken))!;
        Assert.Equal(2, incidentEvidence.OpeningSequence);

        using var invalidLimit = await Send(client, HttpMethod.Get,
            "/api/projects/backups/push-monitors/nightly-backup/reports?limit=101", setup.ManagementToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidLimit.StatusCode);
        using var invalidCursor = await Send(client, HttpMethod.Get,
            "/api/projects/backups/push-monitors/nightly-backup/incidents?before_opening_sequence=0", setup.ManagementToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalidCursor.StatusCode);
        using var anonymous = await Send(client, HttpMethod.Get,
            "/api/projects/backups/push-monitors/nightly-backup/reports");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
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

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(
            $"/api/report/{replacement}/extra", TestContext.Current.CancellationToken)).StatusCode);

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
        var instance = AnInstance.Against(connection, clock: clock, logProvider: logs);
        var setupClient = instance.CreateClient();
        await instance.EstablishAsync("operator@example.test", "a long operator password");
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
