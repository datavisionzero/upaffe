using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Notifications;
using Upaffe.Domain.Projects;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class NotificationIntentTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Http_failure_announces_once_and_recovers_only_smtp_accepted_recipients()
    {
        var connection = await SetupAsync();
        await RunHttpAsync(connection, false, Noon.AddSeconds(1));
        await RunHttpAsync(connection, false, Noon.AddSeconds(2));
        await using (var opened = AnInstance.ContextFor(connection))
        {
            Assert.Single(await opened.Incidents.ToListAsync(TestContext.Current.CancellationToken));
            var alerts = await opened.NotificationDeliveries.ToListAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, alerts.Count);
            Assert.All(alerts, value => Assert.Equal(DeliveryState.Queued, value.State));
            Assert.All(alerts, value => Assert.Equal(NotificationKind.Alert, value.Kind));
        }

        await AcceptOneAsync(connection, Noon.AddSeconds(2), "http");
        await RunHttpAsync(connection, true, Noon.AddSeconds(3));
        await using var resolved = AnInstance.ContextFor(connection);
        var deliveries = await resolved.NotificationDeliveries.OrderBy(value => value.Kind)
            .ThenBy(value => value.Recipient).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, deliveries.Count);
        Assert.Single(deliveries, value => value.Kind == NotificationKind.Recovery);
        Assert.Single(deliveries, value => value.Kind == NotificationKind.Alert && value.State == DeliveryState.Obsolete);
        Assert.Single(deliveries, value => value.Kind == NotificationKind.Alert && value.State == DeliveryState.Accepted);
        Assert.Equal("ops@example.test", deliveries.Single(value => value.Kind == NotificationKind.Recovery).Recipient);
        Assert.False((await resolved.Incidents.SingleAsync(TestContext.Current.CancellationToken)).IsOpen);
    }

    [Fact]
    public async Task A_claimed_alert_is_obsolete_if_the_incident_resolves_before_smtp_submission()
    {
        var connection = await SetupAsync();
        await RunHttpAsync(connection, false, Noon.AddSeconds(1));
        EmailDeliveryLease lease;
        await using (var claim = AnInstance.ContextFor(connection))
            lease = Assert.IsType<EmailDeliveryLease>(await new EmailDeliveryStore(claim)
                .ClaimAsync(Noon.AddSeconds(1), TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken));
        await RunHttpAsync(connection, true, Noon.AddSeconds(2));
        await using var prepare = AnInstance.ContextFor(connection);
        Assert.Equal(EmailPreparation.LeaseLost, await new EmailDeliveryStore(prepare)
            .PrepareAsync(lease.DeliveryId, lease.Token, Noon.AddSeconds(2), TestContext.Current.CancellationToken));
        var row = await prepare.NotificationDeliveries.SingleAsync(value => value.Id == lease.DeliveryId,
            TestContext.Current.CancellationToken);
        Assert.Equal(DeliveryState.Obsolete, row.State);
        Assert.Equal(0, row.AttemptCount);
        Assert.Empty(await prepare.NotificationDeliveries.Where(value =>
            value.Kind == NotificationKind.Recovery).ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Push_explicit_and_missing_failures_open_one_alert_per_recipient()
    {
        var connection = await SetupAsync();
        string token;
        await using (var credentials = AnInstance.ContextFor(connection))
        {
            var issued = await new ReportingCredentialStore(credentials).IssueAsync(
                "systems", "backup", Noon, TestContext.Current.CancellationToken);
            token = Assert.IsType<IssuedReportingCredential>(issued.Issued).Token;
        }
        await SubmitPushAsync(connection, token, Guid.NewGuid(), ReportOutcome.Failure,
            Noon.AddSeconds(1));
        await SubmitPushAsync(connection, token, Guid.NewGuid(), ReportOutcome.Failure,
            Noon.AddSeconds(2));
        await using (var opened = AnInstance.ContextFor(connection))
        {
            Assert.Single(await opened.PushIncidents.ToListAsync(TestContext.Current.CancellationToken));
            Assert.Equal(2, await opened.NotificationDeliveries.CountAsync(TestContext.Current.CancellationToken));
        }
        await AcceptOneAsync(connection, Noon.AddSeconds(2), "push");
        await SubmitPushAsync(connection, token, Guid.NewGuid(), ReportOutcome.Success,
            Noon.AddSeconds(3));
        await using (var resolved = AnInstance.ContextFor(connection))
        {
            Assert.Single(await resolved.NotificationDeliveries.Where(value =>
                value.Kind == NotificationKind.Recovery).ToListAsync(TestContext.Current.CancellationToken));
        }

        await using (var add = AnInstance.ContextFor(connection))
        {
            var project = await add.Projects.SingleAsync(TestContext.Current.CancellationToken);
            add.PushMonitors.Add(PushMonitor.Create(project.Id, "worker", "Worker",
                PushMonitorMode.JobCompletion, 60, 0, null, null, Noon));
            await add.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        PushDeadlineLease due;
        await using (var claim = AnInstance.ContextFor(connection))
            due = Assert.IsType<PushDeadlineLease>(await new PushDeadlineStore(claim).ClaimAsync(
                Noon.AddSeconds(61), TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken));
        await using (var complete = AnInstance.ContextFor(connection))
            Assert.Equal(PushDeadlineCompletion.Completed, await new PushDeadlineStore(complete)
                .CompleteAsync(due, Noon.AddSeconds(61), TestContext.Current.CancellationToken));
        await using var missing = AnInstance.ContextFor(connection);
        var incident = await missing.PushIncidents.SingleAsync(value => value.MonitorId == due.MonitorId,
            TestContext.Current.CancellationToken);
        Assert.Equal("report_missing", incident.OriginalReason);
        Assert.Equal(2, await missing.NotificationDeliveries.CountAsync(value =>
            value.IncidentId == incident.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Recipient_removal_and_monitor_deletion_obsolete_unsent_alerts()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = await SetupAsync();
        await RunHttpAsync(connection, false, Noon.AddSeconds(1));
        await using (var change = AnInstance.ContextFor(connection))
        {
            var result = await new ProjectStore(change).ReplaceRecipientsAsync(
                "systems", ["backup@example.test"], 1, Noon.AddSeconds(2), ct);
            Assert.Equal(ProjectMutation.Changed, result.Outcome);
        }
        await PrepareRecipientAsync(connection, "ops@example.test", Noon.AddSeconds(2),
            EmailPreparation.Obsolete);
        await using (var remove = AnInstance.ContextFor(connection))
        {
            var result = await new HttpMonitorStore(remove).RemoveAsync(
                "systems", "site", 1, Noon.AddSeconds(3), ct);
            Assert.Equal(HttpMonitorMutation.Changed, result.Outcome);
        }
        await PrepareRecipientAsync(connection, "backup@example.test", Noon.AddSeconds(3),
            EmailPreparation.Obsolete);
        await using var inspect = AnInstance.ContextFor(connection);
        Assert.Equal(2, await inspect.NotificationDeliveries.CountAsync(value =>
            value.State == DeliveryState.Obsolete, ct));
    }

    [Fact]
    public async Task Expired_overlapping_windows_reconcile_only_still_open_incidents_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = await SetupAsync();
        await using (var project = AnInstance.ContextFor(connection))
            Assert.Equal(MaintenanceMutation.Changed, (await new MaintenanceStore(project)
                .StartAsync("project", "systems", null, 0, TimeSpan.FromMinutes(10), Noon, ct)).Outcome);
        await using (var monitor = AnInstance.ContextFor(connection))
            Assert.Equal(MaintenanceMutation.Changed, (await new MaintenanceStore(monitor)
                .StartAsync("push", "systems", "backup", 0, TimeSpan.FromMinutes(20), Noon, ct)).Outcome);

        await RunHttpAsync(connection, false, Noon.AddMinutes(1));
        string token;
        await using (var credentials = AnInstance.ContextFor(connection))
            token = Assert.IsType<IssuedReportingCredential>((await new ReportingCredentialStore(credentials)
                .IssueAsync("systems", "backup", Noon, ct)).Issued).Token;
        await SubmitPushAsync(connection, token, Guid.NewGuid(), ReportOutcome.Failure,
            Noon.AddMinutes(2));
        await RunHttpAsync(connection, true, Noon.AddMinutes(3));
        await using (var within = AnInstance.ContextFor(connection))
        {
            Assert.Empty(await within.NotificationDeliveries.ToListAsync(ct));
            Assert.False(await new EmailDeliveryStore(within).ReconcileAsync(Noon.AddMinutes(5), ct));
        }
        await using (var afterProject = AnInstance.ContextFor(connection))
            Assert.False(await new EmailDeliveryStore(afterProject).ReconcileAsync(
                Noon.AddMinutes(11), ct));
        await using (var afterBoth = AnInstance.ContextFor(connection))
            Assert.True(await new EmailDeliveryStore(afterBoth).ReconcileAsync(
                Noon.AddMinutes(21), ct));
        await using (var repeated = AnInstance.ContextFor(connection))
            Assert.False(await new EmailDeliveryStore(repeated).ReconcileAsync(
                Noon.AddMinutes(21), ct));
        await using var restarted = AnInstance.ContextFor(connection);
        Assert.False(await new EmailDeliveryStore(restarted).ReconcileAsync(
            Noon.AddMinutes(22), ct));
        var alerts = await restarted.NotificationDeliveries.ToListAsync(ct);
        Assert.Equal(2, alerts.Count);
        var openPush = await restarted.PushIncidents.SingleAsync(ct);
        Assert.All(alerts, value => Assert.Equal(openPush.Id, value.IncidentId));
        Assert.NotNull(openPush.NotificationDecisionAt);
        var resolvedHttp = await restarted.Incidents.SingleAsync(ct);
        Assert.NotNull(resolvedHttp.NotificationDecisionAt);
    }

    [Fact]
    public async Task Starting_maintenance_defers_claimed_alert_and_announced_recovery_until_end()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = await SetupAsync();
        await RunHttpAsync(connection, false, Noon.AddSeconds(1));
        EmailDeliveryLease lease;
        await using (var claim = AnInstance.ContextFor(connection))
            lease = Assert.IsType<EmailDeliveryLease>(await new EmailDeliveryStore(claim)
                .ClaimAsync(Noon.AddSeconds(1), TimeSpan.FromMinutes(2), ct));
        await using (var start = AnInstance.ContextFor(connection))
            Assert.Equal(MaintenanceMutation.Changed, (await new MaintenanceStore(start)
                .StartAsync("project", "systems", null, 0,
                    TimeSpan.FromMinutes(10), Noon.AddSeconds(2), ct)).Outcome);
        await using (var deferred = AnInstance.ContextFor(connection))
        {
            Assert.Equal(EmailPreparation.Deferred, await new EmailDeliveryStore(deferred)
                .PrepareAsync(lease.DeliveryId, lease.Token, Noon.AddSeconds(2), ct));
            Assert.Equal(0, (await deferred.NotificationDeliveries.SingleAsync(
                value => value.Id == lease.DeliveryId, ct)).AttemptCount);
        }
        await using (var end = AnInstance.ContextFor(connection))
            Assert.Equal(MaintenanceMutation.Changed, (await new MaintenanceStore(end)
                .EndAsync("project", "systems", null, 1,
                    Noon.AddMinutes(1), ct)).Outcome);
        await AcceptOneAsync(connection, Noon.AddMinutes(2), "http");

        await using (var restart = AnInstance.ContextFor(connection))
            Assert.Equal(MaintenanceMutation.Changed, (await new MaintenanceStore(restart)
                .StartAsync("http", "systems", "site", 0,
                    TimeSpan.FromMinutes(10), Noon.AddMinutes(3), ct)).Outcome);
        await RunHttpAsync(connection, true, Noon.AddMinutes(4));
        Guid recoveryId;
        await using (var recovery = AnInstance.ContextFor(connection))
        {
            var row = await recovery.NotificationDeliveries.SingleAsync(value =>
                value.Kind == NotificationKind.Recovery, ct);
            recoveryId = row.Id;
            var token = Guid.NewGuid();
            row.Claim(token, Noon.AddMinutes(4), TimeSpan.FromMinutes(2));
            await recovery.SaveChangesAsync(ct);
            Assert.Equal(EmailPreparation.Deferred, await new EmailDeliveryStore(recovery)
                .PrepareAsync(recoveryId, token, Noon.AddMinutes(4), ct));
        }
        await using (var end = AnInstance.ContextFor(connection))
            Assert.Equal(MaintenanceMutation.Changed, (await new MaintenanceStore(end)
                .EndAsync("http", "systems", "site", 1,
                    Noon.AddMinutes(5), ct)).Outcome);
        await using var after = AnInstance.ContextFor(connection);
        var queued = await after.NotificationDeliveries.SingleAsync(value => value.Id == recoveryId, ct);
        var retryToken = Guid.NewGuid();
        queued.Claim(retryToken, Noon.AddMinutes(6), TimeSpan.FromMinutes(2));
        await after.SaveChangesAsync(ct);
        Assert.Equal(EmailPreparation.Send, await new EmailDeliveryStore(after)
            .PrepareAsync(recoveryId, retryToken, Noon.AddMinutes(6), ct));
    }

    [Fact]
    public async Task Reconciliation_racing_fresh_resolution_leaves_no_sendable_alert()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = await SetupAsync();
        await using (var start = AnInstance.ContextFor(connection))
            Assert.Equal(MaintenanceMutation.Changed, (await new MaintenanceStore(start)
                .StartAsync("project", "systems", null, 0,
                    TimeSpan.FromMinutes(1), Noon, ct)).Outcome);
        await RunHttpAsync(connection, false, Noon.AddSeconds(1));
        await using var reconcileContext = AnInstance.ContextFor(connection);
        await Task.WhenAll(
            new EmailDeliveryStore(reconcileContext).ReconcileAsync(Noon.AddMinutes(2), ct),
            RunHttpAsync(connection, true, Noon.AddMinutes(2)));
        await using var inspect = AnInstance.ContextFor(connection);
        Assert.False((await inspect.Incidents.SingleAsync(ct)).IsOpen);
        Assert.Empty(await inspect.NotificationDeliveries.Where(value =>
            value.State == DeliveryState.Queued || value.State == DeliveryState.Retrying)
            .ToListAsync(ct));
    }

    [Fact]
    public async Task SMTP_acceptance_racing_resolution_still_queues_one_matching_recovery()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = await SetupAsync();
        await RunHttpAsync(connection, false, Noon.AddSeconds(1));
        EmailDeliveryLease lease;
        await using (var claim = AnInstance.ContextFor(connection))
        {
            var store = new EmailDeliveryStore(claim);
            lease = Assert.IsType<EmailDeliveryLease>(await store.ClaimAsync(
                Noon.AddSeconds(1), TimeSpan.FromMinutes(2), ct));
            Assert.Equal(EmailPreparation.Send, await store.PrepareAsync(
                lease.DeliveryId, lease.Token, Noon.AddSeconds(1), ct));
        }
        await RunHttpAsync(connection, true, Noon.AddSeconds(2));
        await using (var complete = AnInstance.ContextFor(connection))
            Assert.True(await new EmailDeliveryStore(complete).CompleteAsync(
                lease.DeliveryId, lease.Token, EmailSendResult.Accepted(),
                Noon.AddSeconds(2), ct));
        await using (var reconcile = AnInstance.ContextFor(connection))
            Assert.True(await new EmailDeliveryStore(reconcile).ReconcileAsync(
                Noon.AddSeconds(3), ct));
        await using var inspect = AnInstance.ContextFor(connection);
        var alert = await inspect.NotificationDeliveries.SingleAsync(value =>
            value.Id == lease.DeliveryId, ct);
        Assert.Equal(DeliveryState.Accepted, alert.State);
        Assert.NotNull(alert.RecoveryDecisionAt);
        var recovery = await inspect.NotificationDeliveries.SingleAsync(value =>
            value.Kind == NotificationKind.Recovery, ct);
        Assert.Equal(alert.Recipient, recovery.Recipient);
        Assert.False(await new EmailDeliveryStore(inspect).ReconcileAsync(
            Noon.AddSeconds(4), ct));
    }

    private static async Task PrepareRecipientAsync(string connection, string recipient,
        DateTimeOffset now, EmailPreparation expected)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var context = AnInstance.ContextFor(connection);
        var delivery = await context.NotificationDeliveries.SingleAsync(value =>
            value.Recipient == recipient && value.Kind == NotificationKind.Alert, ct);
        var token = Guid.NewGuid();
        delivery.Claim(token, now, TimeSpan.FromMinutes(2));
        await context.SaveChangesAsync(ct);
        Assert.Equal(expected, await new EmailDeliveryStore(context).PrepareAsync(
            delivery.Id, token, now, ct));
    }

    private async Task<string> SetupAsync()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var context = AnInstance.ContextFor(connection);
        await AnInstance.MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);
        var project = Project.Create("systems", "Systems", Noon,
            ["ops@example.test", "backup@example.test"]);
        var http = HttpMonitor.Create(project.Id, "site", "Site",
            "https://site.example.test/health", 200, TextCondition.None, null,
            300, 10, 1, null, null, Noon);
        var push = PushMonitor.Create(project.Id, "backup", "Backup",
            PushMonitorMode.JobCompletion, 3600, 0, null, null, Noon);
        context.AddRange(project, http, push,
            HttpMonitorSecret.FromTarget(http.Id, http.TargetUrl));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private static async Task RunHttpAsync(string connection, bool success, DateTimeOffset now)
    {
        var ct = TestContext.Current.CancellationToken;
        StartedHttpTest started;
        await using (var begin = AnInstance.ContextFor(connection))
            started = await new HttpMonitorStore(begin).StartTestAsync("systems", "site", now, ct);
        await using var finish = AnInstance.ContextFor(connection);
        await new HttpMonitorStore(finish).CompleteTestAsync(started.CheckId!.Value,
            success
                ? new HttpExecutionResult(true, null, "Success", 200, 10, "https://site.example.test/health")
                : new HttpExecutionResult(false, "timeout", "Timeout", null, 10, "https://site.example.test/health"),
            now, ct);
    }

    private static async Task SubmitPushAsync(string connection, string token,
        Guid reportId, ReportOutcome outcome, DateTimeOffset now)
    {
        await using var context = AnInstance.ContextFor(connection);
        var result = await new PushReportStore(context).SubmitAsync(token,
            new PushReportSubmission(reportId, now, outcome,
                outcome == ReportOutcome.Failure ? "diagnostic" : null),
            now, TestContext.Current.CancellationToken);
        Assert.Equal(PushReportMutation.Accepted, result.Outcome);
    }

    private static async Task AcceptOneAsync(string connection, DateTimeOffset now, string monitorType)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var context = AnInstance.ContextFor(connection);
        var delivery = await context.NotificationDeliveries.SingleAsync(value =>
            value.MonitorType == monitorType && value.Recipient == "ops@example.test"
            && value.Kind == NotificationKind.Alert, ct);
        var store = new EmailDeliveryStore(context);
        var token = Guid.NewGuid();
        delivery.Claim(token, now, TimeSpan.FromMinutes(2));
        await context.SaveChangesAsync(ct);
        Assert.Equal(EmailPreparation.Send, await store.PrepareAsync(delivery.Id,
            token, now, ct));
        Assert.True(await store.CompleteAsync(delivery.Id, token,
            EmailSendResult.Accepted(), now, ct));
    }
}
