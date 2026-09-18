using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Projects;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class PushDeadlinePersistenceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    [Theory]
    [InlineData(PushMonitorMode.JobCompletion)]
    [InlineData(PushMonitorMode.StateReport)]
    public async Task A_crossed_deadline_fails_each_mode_once_and_survives_restart(PushMonitorMode mode)
    {
        var setup = await EstablishedAsync(mode);
        var deadline = Noon.AddSeconds(90);
        await using (var boundaryContext = AnInstance.ContextFor(setup.ConnectionString))
        {
            Assert.Null(await new PushDeadlineStore(boundaryContext).ClaimAsync(
                deadline,
                LeaseDuration,
                TestContext.Current.CancellationToken));
        }

        PushDeadlineLease lease;
        await using (var firstContext = AnInstance.ContextFor(setup.ConnectionString))
        await using (var secondContext = AnInstance.ContextFor(setup.ConnectionString))
        {
            var claims = await Task.WhenAll(
                new PushDeadlineStore(firstContext).ClaimAsync(deadline.AddDays(120), LeaseDuration, TestContext.Current.CancellationToken),
                new PushDeadlineStore(secondContext).ClaimAsync(deadline.AddDays(120), LeaseDuration, TestContext.Current.CancellationToken));
            lease = Assert.Single(claims, value => value is not null)!;
        }

        var processedAt = deadline.AddDays(120).AddSeconds(1);
        await using (var completionContext = AnInstance.ContextFor(setup.ConnectionString))
        {
            Assert.Equal(PushDeadlineCompletion.Completed, await new PushDeadlineStore(completionContext).CompleteAsync(
                lease,
                processedAt,
                TestContext.Current.CancellationToken));
        }

        await using var restarted = AnInstance.ContextFor(setup.ConnectionString);
        var monitor = await restarted.PushMonitors.SingleAsync(TestContext.Current.CancellationToken);
        var report = await restarted.PushReports.SingleAsync(TestContext.Current.CancellationToken);
        var incident = await restarted.PushIncidents.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(MonitorState.Failing, monitor.State);
        Assert.Null(monitor.LastReceivedAt);
        Assert.Equal(deadline, monitor.NextDeadlineAt);
        Assert.Null(monitor.DeadlineLeaseToken);
        Assert.True(report.IsDeadlineObservation);
        Assert.Equal(deadline, report.ObservedAt);
        Assert.Equal(processedAt, report.ReceivedAt);
        Assert.Equal("report_missing", incident.OriginalReason);
        Assert.Equal(deadline, incident.BeganAt);
        Assert.Equal(processedAt, incident.OpenedAt);
        Assert.Null(await new PushDeadlineStore(restarted).ClaimAsync(
            processedAt.AddDays(1),
            LeaseDuration,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Expired_deadline_work_is_reclaimed_and_only_the_current_lease_completes()
    {
        var setup = await EstablishedAsync(PushMonitorMode.JobCompletion);
        var crossedAt = Noon.AddSeconds(91);
        PushDeadlineLease first;
        await using (var firstContext = AnInstance.ContextFor(setup.ConnectionString))
        {
            first = Assert.IsType<PushDeadlineLease>(await new PushDeadlineStore(firstContext).ClaimAsync(
                crossedAt,
                LeaseDuration,
                TestContext.Current.CancellationToken));
        }

        await using (var activeContext = AnInstance.ContextFor(setup.ConnectionString))
        {
            Assert.Null(await new PushDeadlineStore(activeContext).ClaimAsync(
                crossedAt.AddMinutes(1),
                LeaseDuration,
                TestContext.Current.CancellationToken));
        }

        PushDeadlineLease resumed;
        await using (var resumedContext = AnInstance.ContextFor(setup.ConnectionString))
        {
            resumed = Assert.IsType<PushDeadlineLease>(await new PushDeadlineStore(resumedContext).ClaimAsync(
                crossedAt.AddMinutes(3),
                LeaseDuration,
                TestContext.Current.CancellationToken));
        }

        Assert.Equal(first.MonitorId, resumed.MonitorId);
        Assert.NotEqual(first.Token, resumed.Token);
        await using (var staleContext = AnInstance.ContextFor(setup.ConnectionString))
        {
            Assert.Equal(PushDeadlineCompletion.LeaseLost, await new PushDeadlineStore(staleContext).CompleteAsync(
                first,
                crossedAt.AddMinutes(3).AddSeconds(1),
                TestContext.Current.CancellationToken));
        }

        await using (var currentContext = AnInstance.ContextFor(setup.ConnectionString))
        {
            Assert.Equal(PushDeadlineCompletion.Completed, await new PushDeadlineStore(currentContext).CompleteAsync(
                resumed,
                crossedAt.AddMinutes(3).AddSeconds(2),
                TestContext.Current.CancellationToken));
        }

        await using var inspection = AnInstance.ContextFor(setup.ConnectionString);
        Assert.Equal(1, await inspection.PushReports.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await inspection.PushIncidents.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_fresh_success_racing_completion_supersedes_the_claimed_deadline()
    {
        var setup = await EstablishedAsync(PushMonitorMode.JobCompletion);
        var crossedAt = Noon.AddSeconds(91);
        PushDeadlineLease lease;
        await using (var claimContext = AnInstance.ContextFor(setup.ConnectionString))
        {
            lease = Assert.IsType<PushDeadlineLease>(await new PushDeadlineStore(claimContext).ClaimAsync(
                crossedAt,
                LeaseDuration,
                TestContext.Current.CancellationToken));
        }

        PushReportMutationResult result;
        PushDeadlineCompletion completion;
        await using (var reportContext = AnInstance.ContextFor(setup.ConnectionString))
        await using (var completionContext = AnInstance.ContextFor(setup.ConnectionString))
        {
            var reportTask = new PushReportStore(reportContext).SubmitAsync(
                setup.ReportingToken,
                new PushReportSubmission(Guid.NewGuid(), crossedAt, ReportOutcome.Success, null),
                crossedAt,
                TestContext.Current.CancellationToken);
            var completionTask = new PushDeadlineStore(completionContext).CompleteAsync(
                lease,
                crossedAt.AddSeconds(1),
                TestContext.Current.CancellationToken);
            await Task.WhenAll(reportTask, completionTask);
            result = await reportTask;
            completion = await completionTask;
        }

        Assert.Equal(PushReportMutation.Accepted, result.Outcome);
        Assert.True(result.Receipt!.Applied);
        Assert.Contains(completion, new[] { PushDeadlineCompletion.Completed, PushDeadlineCompletion.Superseded });

        await using var inspection = AnInstance.ContextFor(setup.ConnectionString);
        var monitor = await inspection.PushMonitors.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(MonitorState.Healthy, monitor.State);
        Assert.Equal(crossedAt.AddSeconds(90), monitor.NextDeadlineAt);
        Assert.Null(monitor.DeadlineLeaseToken);
        var reports = await inspection.PushReports.ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains(reports, value => !value.IsDeadlineObservation && value.Outcome == ReportOutcome.Success);
        var incident = await inspection.PushIncidents.SingleOrDefaultAsync(TestContext.Current.CancellationToken);
        Assert.True(incident is null || !incident.IsOpen);
    }

    private async Task<Setup> EstablishedAsync(PushMonitorMode mode)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = AnInstance.ContextFor(connectionString);
        await AnInstance.MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);
        var project = Project.Create("backups", "Backups", Noon);
        var monitor = PushMonitor.Create(
            project.Id,
            "local-report",
            "Local report",
            mode,
            60,
            30,
            null,
            null,
            Noon);
        var credential = ReportingCredential.Create(monitor.Id, Noon);
        var issued = ReportingCredentialSecret.Issue(credential.Id, Noon);
        context.AddRange(project, monitor, credential, issued.Secret);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new(connectionString, issued.Value);
    }

    private sealed record Setup(string ConnectionString, string ReportingToken);
}
