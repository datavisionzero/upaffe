using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Upaffe.Api.Http;
using Upaffe.Application.Access;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Notifications;
using Upaffe.Domain.Projects;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class EmailStatusTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Fact]
    public async Task Status_is_authenticated_filterable_bounded_and_redacted()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = await postgres.CreateDatabaseAsync();
        Guid incidentId;
        await using (var context = AnInstance.ContextFor(connection))
        {
            await AnInstance.MigratorFor(context).ApplyAsync(ct);
            var project = Project.Create("systems", "Systems", Noon,
                ["ops@example.test", "backup@example.test"]);
            var monitor = HttpMonitor.Create(project.Id, "site", "Site",
                "https://site.example.test/health", 200, TextCondition.None, null,
                300, 10, 1, null, null, Noon);
            context.AddRange(project, monitor, HttpMonitorSecret.FromTarget(monitor.Id, monitor.TargetUrl));
            await context.SaveChangesAsync(ct);
        }
        await using (var begin = AnInstance.ContextFor(connection))
        {
            var started = await new HttpMonitorStore(begin).StartTestAsync("systems", "site", Noon, ct);
            await using var complete = AnInstance.ContextFor(connection);
            await new HttpMonitorStore(complete).CompleteTestAsync(started.CheckId!.Value,
                new HttpExecutionResult(false, "timeout", "Timeout", null, 10,
                    "https://site.example.test/health"), Noon.AddSeconds(1), ct);
            incidentId = await complete.Incidents.Select(value => value.Id).SingleAsync(ct);
        }

        await using var instance = AnInstance.Against(connection);
        using var anonymous = instance.CreateClient();
        using var denied = await anonymous.GetAsync("/api/email/deliveries", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var client = instance.CreateClient();
        await instance.EstablishAsync("operator@example.test", "a long operator password");
        using var signIn = await client.PostAsJsonAsync("/api/session",
            new SignInRequest("operator@example.test", "a long operator password"), Json, ct);
        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);

        using var list = await client.GetAsync("/api/email/deliveries?project_key=systems&monitor_type=http&monitor_key=site&limit=1", ct);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var body = await list.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("https://site.example.test/health", body);
        Assert.DoesNotContain("private", body);
        using var page = JsonDocument.Parse(body);
        Assert.Equal(2, page.RootElement.GetProperty("total").GetInt32());
        Assert.True(page.RootElement.GetProperty("has_more").GetBoolean());
        Assert.Equal("queued", page.RootElement.GetProperty("items")[0].GetProperty("state").GetString());
        using var second = await client.GetAsync("/api/email/deliveries?project_key=systems&limit=1&offset=1", ct);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var invalid = await client.GetAsync("/api/email/deliveries?limit=101", ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var incident = await client.GetAsync($"/api/email/incidents/{incidentId}?monitor_type=http", ct);
        Assert.Equal(HttpStatusCode.OK, incident.StatusCode);
        using var incidentBody = JsonDocument.Parse(await incident.Content.ReadAsStringAsync(ct));
        Assert.Equal("pending", incidentBody.RootElement.GetProperty("announcement_state").GetString());
        Assert.Equal(2, incidentBody.RootElement.GetProperty("deliveries").GetArrayLength());
        using var summary = await client.GetAsync("/api/projects/systems/email-summary", ct);
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        using var summaryBody = JsonDocument.Parse(await summary.Content.ReadAsStringAsync(ct));
        Assert.Equal(2, summaryBody.RootElement.GetProperty("pending_count").GetInt32());
        using var missingProject = await client.GetAsync("/api/projects/absent/email-summary", ct);
        Assert.Equal(HttpStatusCode.NotFound, missingProject.StatusCode);

        await using (var maintenance = AnInstance.ContextFor(connection))
            await new MaintenanceStore(maintenance).StartAsync("project", "systems",
                null, 0, TimeSpan.FromMinutes(10), DateTimeOffset.UtcNow, ct);
        using var suppressed = await client.GetAsync($"/api/email/incidents/{incidentId}?monitor_type=http", ct);
        using var suppressedBody = JsonDocument.Parse(await suppressed.Content.ReadAsStringAsync(ct));
        Assert.Equal("suppressed", suppressedBody.RootElement.GetProperty("announcement_state").GetString());
        Assert.Equal("maintenance", suppressedBody.RootElement.GetProperty("suppression_reason").GetString());
    }

    [Fact]
    public async Task Retention_keeps_open_incidents_and_the_latest_maintenance_version()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = await postgres.CreateDatabaseAsync();
        await using var context = AnInstance.ContextFor(connection);
        await AnInstance.MigratorFor(context).ApplyAsync(ct);
        var old = Noon.AddDays(-100);
        var cutoff = Noon.AddDays(-90);
        var project = Project.Create("systems", "Systems", old, ["ops@example.test"]);
        var monitor = HttpMonitor.Create(project.Id, "site", "Site",
            "https://site.example.test/health", 200, TextCondition.None, null,
            300, 10, 1, null, null, old);
        context.AddRange(project, monitor, HttpMonitorSecret.FromTarget(monitor.Id, monitor.TargetUrl));
        await context.SaveChangesAsync(ct);
        var started = await new HttpMonitorStore(context).StartTestAsync("systems", "site", old.AddSeconds(1), ct);
        await new HttpMonitorStore(context).CompleteTestAsync(started.CheckId!.Value,
            new HttpExecutionResult(false, "timeout", "Timeout", null, 10,
                "https://site.example.test/health"), old.AddSeconds(2), ct);
        var openAlert = await context.NotificationDeliveries.SingleAsync(ct);
        var alertToken = Guid.NewGuid();
        openAlert.Claim(alertToken, old.AddSeconds(2), TimeSpan.FromMinutes(2));
        openAlert.BeginAttempt(alertToken, old.AddSeconds(2));
        openAlert.Complete(alertToken, false, false, "smtp_rejected", old.AddSeconds(2));
        await context.SaveChangesAsync(ct);
        var oldFinished = NotificationDelivery.Queue(Guid.NewGuid(), NotificationKind.Recovery,
            "ops@example.test", "systems", "Systems", "site", "Site", "http",
            "recovered", old, old);
        var token = Guid.NewGuid();
        oldFinished.Claim(token, old, TimeSpan.FromMinutes(2));
        oldFinished.BeginAttempt(token, old);
        oldFinished.Complete(token, true, false, null, old);
        var recent = NotificationDelivery.Queue(Guid.NewGuid(), NotificationKind.Recovery,
            "backup@example.test", "systems", "Systems", "site", "Site", "http",
            "recovered", Noon, Noon);
        recent.Claim(token, Noon, TimeSpan.FromMinutes(2));
        recent.BeginAttempt(token, Noon);
        recent.Complete(token, true, false, null, Noon);
        var atBoundary = NotificationDelivery.Queue(Guid.NewGuid(), NotificationKind.Recovery,
            "boundary@example.test", "systems", "Systems", "site", "Site", "http",
            "recovered", cutoff, cutoff);
        atBoundary.Claim(token, cutoff, TimeSpan.FromMinutes(2));
        atBoundary.BeginAttempt(token, cutoff);
        atBoundary.Complete(token, true, false, null, cutoff);
        var awaitingRecovery = NotificationDelivery.Queue(Guid.NewGuid(), NotificationKind.Alert,
            "awaiting@example.test", "systems", "Systems", "site", "Site", "http",
            "timeout", old, old);
        awaitingRecovery.Claim(token, old, TimeSpan.FromMinutes(2));
        awaitingRecovery.BeginAttempt(token, old);
        awaitingRecovery.Complete(token, true, false, null, old);
        var pending = NotificationDelivery.Queue(Guid.NewGuid(), NotificationKind.Recovery,
            "pending@example.test", "systems", "Systems", "site", "Site", "http",
            "recovered", old, old);
        var obsolete = NotificationDelivery.Queue(Guid.NewGuid(), NotificationKind.Recovery,
            "obsolete@example.test", "systems", "Systems", "site", "Site", "http",
            "recovered", old, old);
        obsolete.Obsolete(old);
        context.AddRange(oldFinished, recent, atBoundary, awaitingRecovery, pending, obsolete);
        context.MaintenanceWindows.AddRange(
            MaintenanceWindow.Start("project", project.Id, 1, old, TimeSpan.FromMinutes(1)),
            MaintenanceWindow.Start("project", project.Id, 2, old.AddMinutes(2), TimeSpan.FromMinutes(1)),
            MaintenanceWindow.Start("project", project.Id, 3, cutoff.AddMinutes(-1), TimeSpan.FromMinutes(1)),
            MaintenanceWindow.Start("project", project.Id, 4, Noon, TimeSpan.FromMinutes(1)));
        await context.SaveChangesAsync(ct);
        var result = await new EmailHistoryStore(context).PruneAsync(cutoff, ct);
        Assert.Equal(2, result.DeliveriesDeleted);
        Assert.Equal(2, result.WindowsDeleted);
        var remaining = await context.NotificationDeliveries.AsNoTracking().Select(value => value.Id).ToListAsync(ct);
        Assert.Equal(5, remaining.Count);
        Assert.Contains(recent.Id, remaining);
        Assert.Contains(openAlert.Id, remaining);
        Assert.Contains(atBoundary.Id, remaining);
        Assert.Contains(awaitingRecovery.Id, remaining);
        Assert.Contains(pending.Id, remaining);
        Assert.DoesNotContain(oldFinished.Id, remaining);
        Assert.DoesNotContain(obsolete.Id, remaining);
        Assert.Equal([3, 4], await context.MaintenanceWindows.AsNoTracking()
            .OrderBy(value => value.Version).Select(value => value.Version).ToListAsync(ct));

        await using var resumed = AnInstance.ContextFor(connection);
        Assert.Equal(new EmailHistoryPruneResult(0, 0), await new EmailHistoryStore(resumed)
            .PruneAsync(cutoff, ct));
    }
}
