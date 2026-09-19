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
public sealed class InstanceOverviewTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public async Task Overview_is_authenticated_safe_ordered_and_bounded()
    {
        var connection = await postgres.CreateDatabaseAsync();
        var clock = new MutableTimeProvider(Now);
        await using var instance = AnInstance.Against(connection,
            new Dictionary<string, string?>
            {
                [BootstrapSettings.Variable] = "an-overview-bootstrap-proof-with-enough-entropy",
            }, clock);
        using var client = instance.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });
        using (var bootstrap = await client.PostAsJsonAsync("/api/bootstrap",
            new BootstrapRequest("an-overview-bootstrap-proof-with-enough-entropy",
                "operator@example.test", "a long operator password"),
            TestContext.Current.CancellationToken))
            Assert.Equal(HttpStatusCode.NoContent, bootstrap.StatusCode);
        await SeedAsync(connection);

        using var anonymous = await client.GetAsync("/api/overview",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var signIn = await client.PostAsJsonAsync("/api/session",
            new SignInRequest("operator@example.test", "a long operator password"),
            TestContext.Current.CancellationToken);
        var cookie = Assert.Single(signIn.Headers.GetValues("Set-Cookie")).Split(';', 2)[0];
        using var browserRequest = new HttpRequestMessage(HttpMethod.Get, "/api/overview");
        browserRequest.Headers.Add("Cookie", cookie);
        using var browser = await client.SendAsync(browserRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, browser.StatusCode);
        using var issue = new HttpRequestMessage(HttpMethod.Post, "/api/management-credentials");
        issue.Headers.Add("Cookie", cookie);
        issue.Headers.Add(CsrfProtection.Header, "1");
        issue.Headers.Add("Origin", "http://localhost");
        issue.Content = JsonContent.Create(new CreateCredentialRequest("overview agent"));
        using var issued = await client.SendAsync(issue, TestContext.Current.CancellationToken);
        var credential = (await issued.Content.ReadFromJsonAsync<IssuedCredentialResponse>(
            Json, TestContext.Current.CancellationToken))!;
        using var bearerRequest = new HttpRequestMessage(HttpMethod.Get, "/api/overview");
        bearerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);
        using var bearer = await client.SendAsync(bearerRequest, TestContext.Current.CancellationToken);
        var body = await bearer.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(await browser.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), body);
        Assert.DoesNotContain("secret-query-value", body);
        Assert.DoesNotContain("untrusted diagnostic", body);
        Assert.DoesNotContain(credential.Token, body);

        var overview = JsonSerializer.Deserialize<InstanceOverview>(body, Json)!;
        Assert.Equal(Now, overview.GeneratedAt);
        Assert.Equal(7, overview.Counts.Total);
        Assert.Equal(["alpha", "beta", "empty", "healthy"], overview.Projects.Select(value => value.Key));
        Assert.Equal(["incident", "paused", "failed", "late-http", "late-push"],
            overview.Attention.Select(value => value.Key));
        Assert.Equal("paused", overview.Attention[1].State);
        Assert.NotNull(overview.Attention[1].IncidentId);
        Assert.Null(overview.Attention[1].NextDueAt);
        Assert.Equal("failing", overview.Attention[2].State);
        Assert.Equal("status_mismatch", overview.Attention[2].LatestReason);
        Assert.Null(overview.Attention[2].IncidentId);
        Assert.True(overview.Attention[3].Overdue);
        Assert.Equal("healthy", overview.Attention[3].State);
        Assert.True(overview.Attention[4].Overdue);
        Assert.Equal("untested", overview.Attention[4].State);
        Assert.Null(overview.Attention[4].LatestReason);
        Assert.NotNull(overview.Attention[0].EffectiveMaintenanceUntil);
        Assert.Equal(0, overview.Projects[2].Counts.Total);
        Assert.Equal(1, overview.Projects[3].Counts.Healthy);
        Assert.Equal(3, overview.Delivery.Pending);
        Assert.Equal(1, overview.Delivery.Overdue);
        Assert.Equal(1, overview.Delivery.Retrying);
        Assert.Equal(1, overview.Delivery.TerminalFailure);
        Assert.Equal(1, overview.Delivery.Accepted);

        var counter = new QueryCounter();
        await using var context = new UpaffeDbContext(new DbContextOptionsBuilder<UpaffeDbContext>()
            .UseNpgsql(connection).AddInterceptors(counter).Options);
        var stored = await new InstanceOverviewStore(context).ReadAsync(Now,
            TestContext.Current.CancellationToken);
        Assert.Equal(7, stored.Counts.Total);
        Assert.InRange(counter.Count, 1, 10);
    }

    private static async Task SeedAsync(string connection)
    {
        await using var context = AnInstance.ContextFor(connection);
        var alpha = Project.Create("alpha", "Alpha", Now.AddMinutes(-5), []);
        var beta = Project.Create("beta", "Beta", Now.AddMinutes(-5), []);
        var empty = Project.Create("empty", "Empty", Now.AddMinutes(-5), []);
        var healthyProject = Project.Create("healthy", "Healthy", Now.AddMinutes(-5), []);
        context.Projects.AddRange(alpha, beta, empty, healthyProject);
        var failed = HttpMonitor.Create(alpha.Id, "failed", "Failed",
            "https://example.test/health?token=secret-query-value", 200,
            TextCondition.None, null, 60, 10, 3, null, null, Now.AddMinutes(-2));
        var incident = PushMonitor.Create(alpha.Id, "incident", "Incident",
            PushMonitorMode.StateReport, 60, 0, null, null, Now.AddMinutes(-2));
        var lateHttp = HttpMonitor.Create(beta.Id, "late-http", "Late HTTP",
            "https://example.test/health", 200, TextCondition.None, null,
            60, 10, 1, null, null, Now.AddMinutes(-2));
        var latePush = PushMonitor.Create(beta.Id, "late-push", "Late push",
            PushMonitorMode.JobCompletion, 60, 0, null, null, Now.AddMinutes(-2));
        var healthy = PushMonitor.Create(beta.Id, "healthy", "Healthy",
            PushMonitorMode.StateReport, 60, 0, null, null, Now.AddMinutes(-1));
        var paused = PushMonitor.Create(alpha.Id, "paused", "Paused",
            PushMonitorMode.JobCompletion, 60, 0, null, null, Now.AddMinutes(-2));
        var otherHealthy = PushMonitor.Create(healthyProject.Id, "healthy", "Healthy",
            PushMonitorMode.StateReport, 60, 0, null, null, Now.AddMinutes(-1));
        context.AddRange(failed, incident, lateHttp, latePush, healthy, paused, otherHealthy);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var check = failed.BeginCheck(CheckTrigger.Requested, Now.AddMinutes(-1), Now.AddMinutes(-1));
        check.CompleteFailure("status_mismatch", Now.AddSeconds(-50), 503, 10,
            "https://example.test/health");
        Assert.True(failed.ApplyResult(check, Now.AddSeconds(-50)));
        context.HttpChecks.Add(check);
        var oldCheck = lateHttp.BeginCheck(CheckTrigger.Requested,
            Now.AddSeconds(-80), Now.AddSeconds(-80));
        oldCheck.CompleteSuccess(Now.AddSeconds(-75), 200, 10,
            "https://example.test/health");
        Assert.True(lateHttp.ApplyResult(oldCheck, Now.AddSeconds(-75)));
        context.HttpChecks.Add(oldCheck);
        var failure = incident.Receive(Guid.NewGuid(), Now.AddSeconds(-50),
            Now.AddSeconds(-50), ReportOutcome.Failure, "untrusted diagnostic");
        Assert.True(incident.ApplyReport(failure));
        context.PushReports.Add(failure);
        context.PushIncidents.Add(PushIncident.Open(failure, Now.AddSeconds(-50)));
        var pausedFailure = paused.Receive(Guid.NewGuid(), Now.AddSeconds(-40),
            Now.AddSeconds(-40), ReportOutcome.Failure, null);
        Assert.True(paused.ApplyReport(pausedFailure));
        context.PushReports.Add(pausedFailure);
        context.PushIncidents.Add(PushIncident.Open(pausedFailure, Now.AddSeconds(-40)));
        paused.Pause(Now.AddSeconds(-30));
        var success = healthy.Receive(Guid.NewGuid(), Now.AddSeconds(-10),
            Now.AddSeconds(-10), ReportOutcome.Success, null);
        Assert.True(healthy.ApplyReport(success));
        context.PushReports.Add(success);
        var otherSuccess = otherHealthy.Receive(Guid.NewGuid(), Now.AddSeconds(-10),
            Now.AddSeconds(-10), ReportOutcome.Success, null);
        Assert.True(otherHealthy.ApplyReport(otherSuccess));
        context.PushReports.Add(otherSuccess);
        var queued = NotificationDelivery.Queue(Guid.NewGuid(), NotificationKind.Alert,
            "queued@example.test", alpha.Key, alpha.Name, incident.Key, incident.Name,
            "push", "reported_failure", Now.AddSeconds(-50), Now.AddSeconds(-49));
        var claimed = NotificationDelivery.Queue(Guid.NewGuid(), NotificationKind.Alert,
            "claimed@example.test", alpha.Key, alpha.Name, incident.Key, incident.Name,
            "push", "reported_failure", Now.AddSeconds(-50), Now.AddSeconds(-49));
        var retrying = NotificationDelivery.Queue(Guid.NewGuid(), NotificationKind.Alert,
            "retrying@example.test", alpha.Key, alpha.Name, incident.Key, incident.Name,
            "push", "reported_failure", Now.AddSeconds(-50), Now.AddSeconds(-49));
        var terminal = NotificationDelivery.Queue(Guid.NewGuid(), NotificationKind.Alert,
            "terminal@example.test", alpha.Key, alpha.Name, incident.Key, incident.Name,
            "push", "reported_failure", Now.AddSeconds(-50), Now.AddSeconds(-49));
        var accepted = NotificationDelivery.Queue(Guid.NewGuid(), NotificationKind.Alert,
            "accepted@example.test", alpha.Key, alpha.Name, incident.Key, incident.Name,
            "push", "reported_failure", Now.AddSeconds(-50), Now.AddSeconds(-49));
        claimed.Claim(Guid.NewGuid(), Now.AddSeconds(-45), TimeSpan.FromMinutes(2));
        SetOutcome(retrying, accepted: false, transient: true);
        SetOutcome(terminal, accepted: false, transient: false);
        SetOutcome(accepted, accepted: true, transient: false);
        context.NotificationDeliveries.AddRange(queued, claimed, retrying, terminal, accepted);
        context.MaintenanceWindows.Add(MaintenanceWindow.Start("project", alpha.Id,
            1, Now.AddSeconds(-10), TimeSpan.FromMinutes(10)));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static void SetOutcome(NotificationDelivery delivery, bool accepted, bool transient)
    {
        var token = Guid.NewGuid();
        delivery.Claim(token, Now.AddSeconds(-45), TimeSpan.FromMinutes(2));
        delivery.BeginAttempt(token, Now.AddSeconds(-44));
        delivery.Complete(token, accepted, transient, "smtp_rejected", Now.AddSeconds(-43));
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
