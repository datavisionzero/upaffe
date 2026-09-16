using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Projects;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class HttpCheckEvaluationPersistenceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_failure_is_visible_immediately_but_only_the_threshold_opens_an_incident()
    {
        var connectionString = await EstablishedAsync(failureThreshold: 3);

        var first = await RunAsync(connectionString, Failure("timeout"), Noon.AddSeconds(1));
        var second = await RunAsync(connectionString, Failure("unexpected_status"), Noon.AddSeconds(2));

        Assert.True(first.AppliedToCurrentState);
        Assert.True(second.AppliedToCurrentState);
        await using (var belowThreshold = AnInstance.ContextFor(connectionString))
        {
            var monitor = await belowThreshold.HttpMonitors.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(MonitorState.Failing, monitor.State);
            Assert.Equal(2, monitor.ConsecutiveFailures);
            Assert.Equal(first.CheckId, monitor.FailureStreakStartId);
            Assert.Equal(second.CheckId, monitor.LatestResultId);
            Assert.Null(monitor.LatestSuccessId);
            Assert.Empty(await belowThreshold.Incidents.ToListAsync(TestContext.Current.CancellationToken));
        }

        var opening = await RunAsync(connectionString, Failure("text_missing"), Noon.AddSeconds(3));

        await using var atThreshold = AnInstance.ContextFor(connectionString);
        var incident = await atThreshold.Incidents.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(first.CheckId, incident.FirstFailureCheckId);
        Assert.Equal(opening.CheckId, incident.OpeningCheckId);
        Assert.Equal("timeout", incident.OriginalReason);
        Assert.Equal("text_missing", incident.LatestReason);
    }

    [Fact]
    public async Task Success_resets_the_streak_and_a_repeated_threshold_result_is_idempotent()
    {
        var connectionString = await EstablishedAsync(failureThreshold: 2);
        await RunAsync(connectionString, Failure("timeout"), Noon.AddSeconds(1));
        var success = await RunAsync(connectionString, Success(), Noon.AddSeconds(2));
        var nextFirst = await RunAsync(connectionString, Failure("tls_failure"), Noon.AddSeconds(3));
        var opening = await StartAsync(connectionString, Noon.AddSeconds(4));

        CompletedHttpTest completed;
        await using (var firstCompletion = AnInstance.ContextFor(connectionString))
        {
            completed = await new HttpMonitorStore(firstCompletion).CompleteTestAsync(
                opening.CheckId!.Value,
                Failure("unexpected_status"),
                Noon.AddSeconds(4),
                TestContext.Current.CancellationToken);
        }

        await using (var repeatedCompletion = AnInstance.ContextFor(connectionString))
        {
            var repeated = await new HttpMonitorStore(repeatedCompletion).CompleteTestAsync(
                opening.CheckId.Value,
                Failure("unexpected_status"),
                Noon.AddSeconds(5),
                TestContext.Current.CancellationToken);
            Assert.False(repeated.AppliedToCurrentState);
        }

        await using var inspection = AnInstance.ContextFor(connectionString);
        var monitor = await inspection.HttpMonitors.SingleAsync(TestContext.Current.CancellationToken);
        var incident = await inspection.Incidents.SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(success.AppliedToCurrentState);
        Assert.True(completed.AppliedToCurrentState);
        Assert.Equal(2, monitor.ConsecutiveFailures);
        Assert.Equal(nextFirst.CheckId, monitor.FailureStreakStartId);
        Assert.Equal(nextFirst.CheckId, incident.FirstFailureCheckId);
        Assert.Equal(opening.CheckId, incident.OpeningCheckId);
        Assert.Equal(1, await inspection.Incidents.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Concurrent_and_out_of_order_completion_cannot_overwrite_newer_state_or_open_twice()
    {
        var connectionString = await EstablishedAsync(failureThreshold: 1);
        var older = await StartAsync(connectionString, Noon.AddSeconds(1));
        var newer = await StartAsync(connectionString, Noon.AddSeconds(2));

        await using var newerContext = AnInstance.ContextFor(connectionString);
        await using var olderContext = AnInstance.ContextFor(connectionString);
        var completions = await Task.WhenAll(
            new HttpMonitorStore(newerContext).CompleteTestAsync(
                newer.CheckId!.Value,
                Failure("unexpected_status"),
                Noon.AddSeconds(3),
                TestContext.Current.CancellationToken),
            new HttpMonitorStore(olderContext).CompleteTestAsync(
                older.CheckId!.Value,
                Failure("timeout"),
                Noon.AddSeconds(3),
                TestContext.Current.CancellationToken));

        await using var inspection = AnInstance.ContextFor(connectionString);
        var monitor = await inspection.HttpMonitors.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(newer.CheckId, monitor.LatestResultId);
        Assert.Equal(MonitorState.Failing, monitor.State);
        Assert.InRange(completions.Count(value => value.AppliedToCurrentState), 1, 2);
        Assert.Equal(1, await inspection.Incidents.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Further_failures_update_the_open_incident_without_replacing_its_origin()
    {
        var connectionString = await EstablishedAsync(failureThreshold: 2);
        var first = await RunAsync(connectionString, Failure("timeout"), Noon.AddSeconds(1));
        var opening = await RunAsync(connectionString, Failure("unexpected_status"), Noon.AddSeconds(2));
        var latest = await RunAsync(connectionString, Failure("text_missing"), Noon.AddSeconds(3));

        await using var inspection = AnInstance.ContextFor(connectionString);
        var incident = await inspection.Incidents.SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(incident.IsOpen);
        Assert.Equal(first.CheckId, incident.FirstFailureCheckId);
        Assert.Equal(opening.CheckId, incident.OpeningCheckId);
        Assert.Equal(latest.CheckId, incident.LatestFailureCheckId);
        Assert.Equal("timeout", incident.OriginalReason);
        Assert.Equal("text_missing", incident.LatestReason);
        Assert.Equal(Noon.AddSeconds(3), incident.LastObservedAt);
    }

    [Fact]
    public async Task A_fresh_success_resolves_once_and_preserves_the_failure_history()
    {
        var connectionString = await EstablishedAsync(failureThreshold: 1);
        var failure = await RunAsync(connectionString, Failure("timeout"), Noon.AddSeconds(1));
        var success = await StartAsync(connectionString, Noon.AddSeconds(2));

        await using var firstContext = AnInstance.ContextFor(connectionString);
        await using var repeatedContext = AnInstance.ContextFor(connectionString);
        var resolutions = await Task.WhenAll(
            new HttpMonitorStore(firstContext).CompleteTestAsync(
                success.CheckId!.Value,
                Success(),
                Noon.AddSeconds(3),
                TestContext.Current.CancellationToken),
            new HttpMonitorStore(repeatedContext).CompleteTestAsync(
                success.CheckId.Value,
                Success(),
                Noon.AddSeconds(3),
                TestContext.Current.CancellationToken));

        await using var inspection = AnInstance.ContextFor(connectionString);
        var monitor = await inspection.HttpMonitors.SingleAsync(TestContext.Current.CancellationToken);
        var incident = await inspection.Incidents.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, resolutions.Count(value => value.AppliedToCurrentState));
        Assert.Equal(MonitorState.Healthy, monitor.State);
        Assert.Equal(success.CheckId, monitor.LatestResultId);
        Assert.Equal(success.CheckId, monitor.LatestSuccessId);
        Assert.False(incident.IsOpen);
        Assert.Equal(failure.CheckId, incident.FirstFailureCheckId);
        Assert.Equal(failure.CheckId, incident.OpeningCheckId);
        Assert.Equal(failure.CheckId, incident.LatestFailureCheckId);
        Assert.Equal(success.CheckId, incident.ResolutionCheckId);
        Assert.Equal(Noon.AddSeconds(3), incident.ResolvedAt);
        Assert.Equal("timeout", incident.OriginalReason);
        Assert.Equal("timeout", incident.LatestReason);
    }

    [Fact]
    public async Task A_late_success_cannot_resolve_an_incident_opened_by_a_newer_failure()
    {
        var connectionString = await EstablishedAsync(failureThreshold: 1);
        var olderSuccess = await StartAsync(connectionString, Noon.AddSeconds(1));
        var newerFailure = await StartAsync(connectionString, Noon.AddSeconds(2));

        await using (var failureContext = AnInstance.ContextFor(connectionString))
        {
            var applied = await new HttpMonitorStore(failureContext).CompleteTestAsync(
                newerFailure.CheckId!.Value,
                Failure("unexpected_status"),
                Noon.AddSeconds(3),
                TestContext.Current.CancellationToken);
            Assert.True(applied.AppliedToCurrentState);
        }

        await using (var successContext = AnInstance.ContextFor(connectionString))
        {
            var ignored = await new HttpMonitorStore(successContext).CompleteTestAsync(
                olderSuccess.CheckId!.Value,
                Success(),
                Noon.AddSeconds(4),
                TestContext.Current.CancellationToken);
            Assert.False(ignored.AppliedToCurrentState);
        }

        await using var inspection = AnInstance.ContextFor(connectionString);
        var incident = await inspection.Incidents.SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(incident.IsOpen);
        Assert.Null(incident.ResolutionCheckId);
        Assert.Null(incident.ResolvedAt);
    }

    [Fact]
    public async Task A_check_racing_with_pause_has_one_of_two_serial_results_and_never_unpauses_the_monitor()
    {
        var connectionString = await EstablishedAsync(failureThreshold: 1);
        var started = await StartAsync(connectionString, Noon.AddSeconds(1));
        await using var pauseContext = AnInstance.ContextFor(connectionString);
        await using var completionContext = AnInstance.ContextFor(connectionString);

        var pauseTask = new HttpMonitorStore(pauseContext).PauseAsync(
            "public-services",
            "public-site",
            1,
            Noon.AddSeconds(2),
            TestContext.Current.CancellationToken);
        var completionTask = new HttpMonitorStore(completionContext).CompleteTestAsync(
            started.CheckId!.Value,
            Failure("timeout"),
            Noon.AddSeconds(2),
            TestContext.Current.CancellationToken);
        await Task.WhenAll(pauseTask, completionTask);
        var pause = await pauseTask;
        var completion = await completionTask;

        Assert.Equal(HttpMonitorMutation.Changed, pause.Outcome);
        await using var inspection = AnInstance.ContextFor(connectionString);
        var monitor = await inspection.HttpMonitors.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(MonitorState.Paused, monitor.State);
        Assert.Null(monitor.NextCheckAt);
        Assert.True(await inspection.HttpChecks.SingleAsync(TestContext.Current.CancellationToken) is { IsCompleted: true });
        Assert.Equal(
            completion.AppliedToCurrentState ? 1 : 0,
            await inspection.Incidents.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_pre_pause_check_racing_with_resume_cannot_evaluate_the_new_generation()
    {
        var connectionString = await EstablishedAsync(failureThreshold: 1);
        var started = await StartAsync(connectionString, Noon.AddSeconds(1));
        await using (var pauseContext = AnInstance.ContextFor(connectionString))
        {
            var paused = await new HttpMonitorStore(pauseContext).PauseAsync(
                "public-services",
                "public-site",
                1,
                Noon.AddSeconds(2),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpMonitorMutation.Changed, paused.Outcome);
        }

        await using var resumeContext = AnInstance.ContextFor(connectionString);
        await using var completionContext = AnInstance.ContextFor(connectionString);
        var resumeTask = new HttpMonitorStore(resumeContext).ResumeAsync(
            "public-services",
            "public-site",
            2,
            Noon.AddSeconds(3),
            TestContext.Current.CancellationToken);
        var completionTask = new HttpMonitorStore(completionContext).CompleteTestAsync(
            started.CheckId!.Value,
            Failure("timeout"),
            Noon.AddSeconds(3),
            TestContext.Current.CancellationToken);
        await Task.WhenAll(resumeTask, completionTask);

        Assert.Equal(HttpMonitorMutation.Changed, (await resumeTask).Outcome);
        Assert.False((await completionTask).AppliedToCurrentState);
        await using var inspection = AnInstance.ContextFor(connectionString);
        var monitor = await inspection.HttpMonitors.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(MonitorState.Untested, monitor.State);
        Assert.Equal(0, monitor.ConsecutiveFailures);
        Assert.Equal(Noon.AddSeconds(3), monitor.NextCheckAt);
        Assert.Empty(await inspection.Incidents.ToListAsync(TestContext.Current.CancellationToken));
    }

    private async Task<CompletedHttpTest> RunAsync(
        string connectionString,
        HttpExecutionResult result,
        DateTimeOffset now)
    {
        var started = await StartAsync(connectionString, now);
        await using var context = AnInstance.ContextFor(connectionString);
        return await new HttpMonitorStore(context).CompleteTestAsync(
            started.CheckId!.Value,
            result,
            now,
            TestContext.Current.CancellationToken);
    }

    private static async Task<StartedHttpTest> StartAsync(string connectionString, DateTimeOffset now)
    {
        await using var context = AnInstance.ContextFor(connectionString);
        var started = await new HttpMonitorStore(context).StartTestAsync(
            "public-services",
            "public-site",
            now,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpMonitorMutation.Changed, started.Outcome);
        return started;
    }

    private async Task<string> EstablishedAsync(int failureThreshold)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = AnInstance.ContextFor(connectionString);
        await AnInstance.MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);
        var project = Project.Create("public-services", "Public services", Noon);
        var monitor = HttpMonitor.Create(
            project.Id,
            "public-site",
            "Public site",
            "https://status.example.test/health",
            200,
            TextCondition.None,
            null,
            300,
            10,
            failureThreshold,
            null,
            null,
            Noon);
        context.AddRange(project, monitor, HttpMonitorSecret.FromTarget(monitor.Id, monitor.TargetUrl));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return connectionString;
    }

    private static HttpExecutionResult Failure(string reason) =>
        new(false, reason, "The check failed.", 503, 25, "https://status.example.test/health");

    private static HttpExecutionResult Success() =>
        new(true, null, "The check succeeded.", 200, 20, "https://status.example.test/health");
}
