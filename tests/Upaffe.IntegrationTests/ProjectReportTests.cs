using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Upaffe.Api.Http;
using Upaffe.Application.Access;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Notifications;
using Upaffe.Domain.Projects;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class ProjectReportTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public async Task Report_is_safe_consistent_and_bounded_for_many_monitors()
    {
        var connection = await postgres.CreateDatabaseAsync();
        var clock = new MutableTimeProvider(Now);
        await using var instance = AnInstance.Against(connection,
            new Dictionary<string, string?>
            {
                [BootstrapSettings.Variable] = "a-project-report-bootstrap-proof-with-enough-entropy",
            }, clock);
        using var client = instance.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });
        using (var bootstrap = await client.PostAsJsonAsync("/api/bootstrap",
            new BootstrapRequest("a-project-report-bootstrap-proof-with-enough-entropy",
                "operator@example.test", "a long operator password"),
            TestContext.Current.CancellationToken))
            Assert.Equal(HttpStatusCode.NoContent, bootstrap.StatusCode);

        await SeedAsync(connection);
        using var signIn = await client.PostAsJsonAsync("/api/session",
            new SignInRequest("operator@example.test", "a long operator password"),
            TestContext.Current.CancellationToken);
        var cookie = Assert.Single(signIn.Headers.GetValues("Set-Cookie"))
            .Split(';', 2)[0];
        using var issue = new HttpRequestMessage(HttpMethod.Post, "/api/management-credentials");
        issue.Headers.Add("Cookie", cookie);
        issue.Headers.Add(CsrfProtection.Header, "1");
        issue.Headers.Add("Origin", "http://localhost");
        issue.Content = JsonContent.Create(new CreateCredentialRequest("report agent"));
        using var issued = await client.SendAsync(issue, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var credential = (await issued.Content.ReadFromJsonAsync<IssuedCredentialResponse>(
            Json, TestContext.Current.CancellationToken))!;

        using var anonymous = await client.GetAsync("/api/projects/example/report",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var browserRequest = new HttpRequestMessage(HttpMethod.Get,
            "/api/projects/example/report");
        browserRequest.Headers.Add("Cookie", cookie);
        using var browser = await client.SendAsync(browserRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, browser.StatusCode);
        using var bearerRequest = new HttpRequestMessage(HttpMethod.Get,
            "/api/projects/example/report");
        bearerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);
        using var bearer = await client.SendAsync(bearerRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, bearer.StatusCode);
        var browserJson = await browser.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var bearerJson = await bearer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(browserJson, bearerJson);
        Assert.DoesNotContain("secret-query-value", bearerJson);
        Assert.DoesNotContain("untrusted diagnostic text", bearerJson);
        Assert.DoesNotContain(credential.Token, bearerJson);

        var report = JsonSerializer.Deserialize<ProjectReport>(bearerJson, Json)!;
        Assert.Equal(34, report.Counts.Total);
        Assert.Equal(30, report.Healthy.Count);
        Assert.Equal(["push", "http", "push", "push"],
            report.Attention.Select(value => value.Type).ToArray());
        var failed = Assert.Single(report.Attention, value => value.Key == "failed-http");
        Assert.Equal("failing", failed.State);
        Assert.Equal(1, failed.FailureCount);
        Assert.Null(failed.Incident);
        Assert.Equal("status_mismatch", failed.LatestResult?.Reason);
        Assert.Equal("https://example.test/health", failed.TargetUrl);
        var missing = Assert.Single(report.Attention, value => value.Key == "missing-push");
        Assert.Equal("report_missing", missing.LatestResult?.Reason);
        Assert.Equal(Now.AddSeconds(-30), missing.LastReceivedAt);
        Assert.NotNull(missing.LastSuccess);
        Assert.NotEqual(missing.LatestResult?.Id, missing.LastSuccess.Id);
        Assert.NotNull(missing.Incident);
        Assert.Equal("Inspect the backup log", missing.Instruction);
        Assert.NotNull(report.ProjectMaintenance);
        Assert.NotNull(missing.EffectiveMaintenanceUntil);
        var paused = Assert.Single(report.Attention, value => value.Key == "paused-push");
        Assert.Equal("paused", paused.State);
        Assert.Null(paused.NextDueAt);
        Assert.Null(paused.LatestResult);
        Assert.NotNull(paused.LastSuccess);
        Assert.Equal(0, report.Email.Delivery.TerminalFailureCount);

        var counter = new QueryCounter();
        await using var counted = new UpaffeDbContext(new DbContextOptionsBuilder<UpaffeDbContext>()
            .UseNpgsql(connection).AddInterceptors(counter).Options);
        var store = new ProjectReportStore(counted, new EmailStatusStore(counted));
        var again = await store.ReadAsync("example", Now, TestContext.Current.CancellationToken);
        Assert.Equal(34, again?.Counts.Total);
        Assert.InRange(counter.Count, 1, 17);

        using var missingProject = new HttpRequestMessage(HttpMethod.Get,
            "/api/projects/no-such-project/report");
        missingProject.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);
        using var missingResponse = await client.SendAsync(missingProject,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        await using (var context = AnInstance.ContextFor(connection))
        {
            var project = await context.Projects.SingleAsync(value => value.Key == "example",
                TestContext.Current.CancellationToken);
            project.Delete(Now);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        using var deletedRequest = new HttpRequestMessage(HttpMethod.Get,
            "/api/projects/example/report");
        deletedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);
        using var deletedResponse = await client.SendAsync(deletedRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, deletedResponse.StatusCode);

        using var revoke = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/management-credentials/{credential.Id}");
        revoke.Headers.Add("Cookie", cookie);
        revoke.Headers.Add(CsrfProtection.Header, "1");
        revoke.Headers.Add("Origin", "http://localhost");
        using var revoked = await client.SendAsync(revoke,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using var rejectedRequest = new HttpRequestMessage(HttpMethod.Get,
            "/api/projects/example/report");
        rejectedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);
        using var rejected = await client.SendAsync(rejectedRequest,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    private static async Task SeedAsync(string connection)
    {
        await using var context = AnInstance.ContextFor(connection);
        var before = Now.AddSeconds(-10);
        var project = Project.Create("example", "Example", before,
            ["ops@example.test"]);
        context.Projects.Add(project);
        var failed = HttpMonitor.Create(project.Id, "failed-http", "Failed HTTP",
            "https://example.test/health?token=secret-query-value", 200,
            TextCondition.None, null, 60, 10, 3, "Check the endpoint",
            "https://docs.example.test/runbooks/http", before);
        var missing = PushMonitor.Create(project.Id, "missing-push", "Missing push",
            PushMonitorMode.JobCompletion, 30, 0, "Inspect the backup log",
            "https://docs.example.test/runbooks/backup", Now.AddSeconds(-100));
        var paused = PushMonitor.Create(project.Id, "paused-push", "Paused push",
            PushMonitorMode.StateReport, 60, 0, null, null, before);
        context.AddRange(failed, missing, paused);
        context.PushMonitors.Add(PushMonitor.Create(project.Id, "untested-push",
            "Untested push", PushMonitorMode.JobCompletion, 60, 0,
            null, null, before));
        var healthy = new List<PushMonitor>();
        for (var index = 0; index < 30; index++)
        {
            var monitor = PushMonitor.Create(project.Id, $"healthy-{index:00}",
                $"Healthy {index:00}", PushMonitorMode.StateReport, 60, 0,
                null, null, before);
            healthy.Add(monitor);
            context.PushMonitors.Add(monitor);
        }
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var check = failed.BeginCheck(CheckTrigger.Requested, before, before);
        check.CompleteFailure("status_mismatch", before.AddSeconds(1), 503, 12,
            "https://example.test/health");
        Assert.True(failed.ApplyResult(check, before.AddSeconds(1)));
        context.HttpChecks.Add(check);
        var success = missing.Receive(Guid.NewGuid(), Now.AddSeconds(-80),
            Now.AddSeconds(-80), ReportOutcome.Success, null);
        Assert.True(missing.ApplyReport(success));
        var missed = missing.MissDeadline(Now.AddSeconds(-40));
        Assert.True(missing.ApplyReport(missed));
        var late = missing.Receive(Guid.NewGuid(), Now.AddSeconds(-90),
            Now.AddSeconds(-30), ReportOutcome.Success, null);
        Assert.False(missing.ApplyReport(late));
        context.AddRange(success, missed, late,
            PushIncident.Open(missed, Now.AddSeconds(-40)));
        var previous = paused.Receive(Guid.NewGuid(), before, before,
            ReportOutcome.Success, null);
        Assert.True(paused.ApplyReport(previous));
        paused.Pause(Now.AddSeconds(-5));
        paused.Resume(Now.AddSeconds(-4));
        paused.Pause(Now.AddSeconds(-3));
        context.PushReports.Add(previous);
        foreach (var monitor in healthy)
        {
            var report = monitor.Receive(Guid.NewGuid(), before, before,
                ReportOutcome.Success, null);
            Assert.True(monitor.ApplyReport(report));
            context.PushReports.Add(report);
        }
        context.MaintenanceWindows.Add(MaintenanceWindow.Start("project", project.Id,
            1, before, TimeSpan.FromMinutes(10)));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
