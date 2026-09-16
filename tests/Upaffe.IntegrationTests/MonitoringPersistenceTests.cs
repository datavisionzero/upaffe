using Microsoft.EntityFrameworkCore;
using Npgsql;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Projects;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class MonitoringPersistenceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Monitor_configuration_and_secrets_round_trip_through_separate_reads()
    {
        await using var context = await MigratedContextAsync();
        var project = Project.Create("public-services", "Public services", Noon);
        var monitor = NewMonitor(project.Id, "https://status.example.test/health?token=target-secret");
        var monitorSecret = HttpMonitorSecret.FromTarget(
            monitor.Id,
            "https://status.example.test/health?token=target-secret");
        var (header, headerSecret) = HttpMonitorHeader.Create(
            monitor.Id,
            "Authorization",
            "Bearer header-secret",
            Noon);
        context.AddRange(project, monitor, monitorSecret, header, headerSecret);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var ordinary = await context.HttpMonitors.SingleAsync(TestContext.Current.CancellationToken);
        var metadata = await context.HttpMonitorHeaders.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("https://status.example.test/health", ordinary.TargetUrl);
        Assert.True(ordinary.HasTargetQuery);
        Assert.Equal("authorization", metadata.Name);
        Assert.DoesNotContain("target-secret", ordinary.TargetUrl, StringComparison.Ordinal);

        var explicitTargetSecret = await context.HttpMonitorSecrets.SingleAsync(TestContext.Current.CancellationToken);
        var explicitHeaderSecret = await context.HttpMonitorHeaderSecrets.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("?token=target-secret", explicitTargetSecret.RevealTargetQuery());
        Assert.Equal("Bearer header-secret", explicitHeaderSecret.Reveal());

        var ordinaryColumns = await context.Database.SqlQuery<string>(
            $"""
            select column_name::text as "Value"
            from information_schema.columns
            where table_schema = 'public' and table_name in ('http_monitor', 'http_monitor_header')
            """).ToListAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("target_query_utf8", ordinaryColumns);
        Assert.DoesNotContain("value_utf8", ordinaryColumns);
    }

    [Fact]
    public async Task Configuration_change_pause_and_resume_keep_identity_and_history_facts()
    {
        await using var context = await MigratedContextAsync();
        var project = Project.Create("public-services", "Public services", Noon);
        var monitor = NewMonitor(project.Id);
        var secret = HttpMonitorSecret.FromTarget(monitor.Id, "https://status.example.test/health");
        context.AddRange(project, monitor, secret);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var id = monitor.Id;
        monitor.ChangeConfiguration(
            "Renamed public site",
            "https://new.example.test/ready?proof=write-only",
            204,
            TextCondition.Forbidden,
            "maintenance",
            600,
            20,
            2,
            "Check the new deployment.",
            "https://runbooks.example.test/new-site",
            Noon.AddMinutes(1));
        secret.ReplaceTarget("https://new.example.test/ready?proof=write-only");
        monitor.Pause(Noon.AddMinutes(2));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var paused = await context.HttpMonitors.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(id, paused.Id);
        Assert.Equal("public-site", paused.Key);
        Assert.Equal(MonitorState.Paused, paused.State);
        Assert.Null(paused.NextCheckAt);
        Assert.Equal(3, paused.Version);

        paused.Resume(Noon.AddMinutes(3));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var resumed = await context.HttpMonitors.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(MonitorState.Untested, resumed.State);
        Assert.Equal(2, resumed.EvaluationGeneration);
        Assert.Equal(Noon.AddMinutes(3), resumed.NextCheckAt);
        Assert.Equal(4, resumed.Version);
    }

    [Fact]
    public async Task Latest_result_and_latest_success_are_distinct_persisted_facts()
    {
        await using var context = await MigratedContextAsync();
        var (project, monitor) = await AddMonitorAsync(context);

        var success = monitor.BeginCheck(CheckTrigger.Scheduled, Noon, Noon);
        Claim(success, Noon);
        success.CompleteSuccess(Noon.AddSeconds(1), 200, 25, "https://status.example.test/health?redacted=yes");
        context.HttpChecks.Add(success);
        Assert.True(monitor.ApplyResult(success, Noon.AddSeconds(1)));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var failure = monitor.BeginCheck(CheckTrigger.Scheduled, Noon.AddMinutes(5), Noon.AddMinutes(5));
        Claim(failure, Noon.AddMinutes(5));
        failure.CompleteFailure(
            "unexpected_status",
            Noon.AddMinutes(5).AddSeconds(1),
            503,
            30,
            "https://status.example.test/health?redacted=again");
        context.HttpChecks.Add(failure);
        Assert.True(monitor.ApplyResult(failure, Noon.AddMinutes(5).AddSeconds(1)));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var stored = await context.HttpMonitors.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(project.Id, stored.ProjectId);
        Assert.Equal(MonitorState.Failing, stored.State);
        Assert.Equal(failure.Id, stored.LatestResultId);
        Assert.Equal(success.Id, stored.LatestSuccessId);
        Assert.Equal(1, stored.ConsecutiveFailures);
        Assert.DoesNotContain("redacted", failure.EffectiveUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostgreSQL_allows_only_one_open_incident_per_monitor_and_preserves_resolution()
    {
        await using var context = await MigratedContextAsync();
        var (_, monitor) = await AddMonitorAsync(context);
        var first = FailedCheck(monitor, Noon, "timeout");
        var opening = FailedCheck(monitor, Noon.AddSeconds(2), "unexpected_status");
        context.HttpChecks.AddRange(first, opening);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var incident = Incident.Open(first, opening, Noon.AddSeconds(4));
        context.Incidents.Add(incident);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var duplicate = Incident.Open(first, opening, Noon.AddSeconds(4));
        context.Incidents.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(TestContext.Current.CancellationToken));
        context.Entry(duplicate).State = EntityState.Detached;

        var success = monitor.BeginCheck(CheckTrigger.Scheduled, Noon.AddSeconds(5), Noon.AddSeconds(5));
        Claim(success, Noon.AddSeconds(5));
        success.CompleteSuccess(Noon.AddSeconds(6), 200, 15, "https://status.example.test/health");
        context.HttpChecks.Add(success);
        incident.Resolve(success);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var stored = await context.Incidents.SingleAsync(TestContext.Current.CancellationToken);
        Assert.False(stored.IsOpen);
        Assert.Equal(first.CompletedAt, stored.BeganAt);
        Assert.Equal(success.Id, stored.ResolutionCheckId);
        Assert.Equal(success.CompletedAt, stored.ResolvedAt);
    }

    [Fact]
    public async Task PostgreSQL_rejects_invalid_threshold_and_time_state()
    {
        await using var context = await MigratedContextAsync();
        var (_, monitor) = await AddMonitorAsync(context);

        var thresholdFailure = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlAsync(
                $"update http_monitor set failure_threshold = 0 where id = {monitor.Id}",
                TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, thresholdFailure.SqlState);

        var pauseFailure = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlAsync(
                $"update http_monitor set state = 'Paused', paused_at = null where id = {monitor.Id}",
                TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, pauseFailure.SqlState);
    }

    private async Task<(Project Project, HttpMonitor Monitor)> AddMonitorAsync(UpaffeDbContext context)
    {
        var project = Project.Create("public-services", "Public services", Noon);
        var monitor = NewMonitor(project.Id);
        context.AddRange(project, monitor, HttpMonitorSecret.FromTarget(monitor.Id, monitor.TargetUrl));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (project, monitor);
    }

    private static HttpMonitor NewMonitor(Guid projectId, string target = "https://status.example.test/health") =>
        HttpMonitor.Create(
            projectId,
            "public-site",
            "Public site",
            target,
            200,
            TextCondition.Required,
            "ready",
            300,
            10,
            3,
            "Check the deployment.",
            "https://runbooks.example.test/public-site",
            Noon);

    private static HttpCheck FailedCheck(HttpMonitor monitor, DateTimeOffset at, string reason)
    {
        var check = monitor.BeginCheck(CheckTrigger.Scheduled, at, at);
        Claim(check, at);
        check.CompleteFailure(reason, at.AddSeconds(1), null, 1_000, null);
        return check;
    }

    private static void Claim(HttpCheck check, DateTimeOffset at) =>
        check.ClaimExecution(Guid.NewGuid(), at, at.AddMinutes(2));

    private async Task<UpaffeDbContext> MigratedContextAsync()
    {
        var context = AnInstance.ContextFor(await postgres.CreateDatabaseAsync());
        await AnInstance.MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);
        return context;
    }
}
