using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Projects;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class ScheduledHttpCheckPersistenceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task Concurrent_instances_claim_one_due_run_and_persist_one_result()
    {
        var connectionString = await EstablishedAsync();
        await using var firstContext = AnInstance.ContextFor(connectionString);
        await using var secondContext = AnInstance.ContextFor(connectionString);
        var firstStore = new ScheduledHttpCheckStore(firstContext);
        var secondStore = new ScheduledHttpCheckStore(secondContext);

        var claims = await Task.WhenAll(
            firstStore.ClaimAsync(Noon, LeaseDuration, TestContext.Current.CancellationToken),
            secondStore.ClaimAsync(Noon, LeaseDuration, TestContext.Current.CancellationToken));
        var lease = Assert.Single(claims, value => value is not null)!;

        Assert.Equal("https://status.example.test/health?token=target-secret", lease.Request.TargetUrl);
        Assert.Equal(new HttpExecutionHeader("authorization", "Bearer header-secret"), Assert.Single(lease.Request.Headers));

        await using var completionContext = AnInstance.ContextFor(connectionString);
        var completion = await new ScheduledHttpCheckStore(completionContext).CompleteAsync(
            lease.CheckId,
            lease.Token,
            new(true, null, "The check succeeded.", 200, 25, "https://status.example.test/health"),
            Noon.AddSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(ScheduledHttpCheckCompletion.Completed, completion);
        await using var inspection = AnInstance.ContextFor(connectionString);
        var check = await inspection.HttpChecks.SingleAsync(TestContext.Current.CancellationToken);
        var monitor = await inspection.HttpMonitors.SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(check.IsCompleted);
        Assert.Equal(1, check.ExecutionAttempts);
        Assert.Equal(Noon.AddMinutes(5), monitor.NextCheckAt);
    }

    [Fact]
    public async Task Expired_work_is_reclaimed_and_only_the_current_lease_can_complete_it()
    {
        var connectionString = await EstablishedAsync();
        ScheduledHttpCheckLease first;
        await using (var firstContext = AnInstance.ContextFor(connectionString))
        {
            first = Assert.IsType<ScheduledHttpCheckLease>(await new ScheduledHttpCheckStore(firstContext).ClaimAsync(
                Noon,
                LeaseDuration,
                TestContext.Current.CancellationToken));
        }

        ScheduledHttpCheckLease resumed;
        await using (var resumedContext = AnInstance.ContextFor(connectionString))
        {
            resumed = Assert.IsType<ScheduledHttpCheckLease>(await new ScheduledHttpCheckStore(resumedContext).ClaimAsync(
                Noon.AddMinutes(3),
                LeaseDuration,
                TestContext.Current.CancellationToken));
        }

        Assert.Equal(first.CheckId, resumed.CheckId);
        Assert.NotEqual(first.Token, resumed.Token);

        await using (var staleContext = AnInstance.ContextFor(connectionString))
        {
            var stale = await new ScheduledHttpCheckStore(staleContext).CompleteAsync(
                first.CheckId,
                first.Token,
                Success(),
                Noon.AddMinutes(3).AddSeconds(1),
                TestContext.Current.CancellationToken);
            Assert.Equal(ScheduledHttpCheckCompletion.LeaseLost, stale);
        }

        await using (var currentContext = AnInstance.ContextFor(connectionString))
        {
            var completed = await new ScheduledHttpCheckStore(currentContext).CompleteAsync(
                resumed.CheckId,
                resumed.Token,
                Success(),
                Noon.AddMinutes(3).AddSeconds(2),
                TestContext.Current.CancellationToken);
            Assert.Equal(ScheduledHttpCheckCompletion.Completed, completed);
        }

        await using var inspection = AnInstance.ContextFor(connectionString);
        var check = await inspection.HttpChecks.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, check.ExecutionAttempts);
        Assert.True(check.IsCompleted);
        Assert.Equal(1, await inspection.HttpChecks.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Paused_and_removed_monitors_are_skipped_while_resume_is_due_immediately()
    {
        var connectionString = await EstablishedAsync(paused: true);
        await using (var pausedContext = AnInstance.ContextFor(connectionString))
        {
            Assert.Null(await new ScheduledHttpCheckStore(pausedContext).ClaimAsync(
                Noon.AddMinutes(1),
                LeaseDuration,
                TestContext.Current.CancellationToken));
        }

        await using (var resumeContext = AnInstance.ContextFor(connectionString))
        {
            var monitor = await resumeContext.HttpMonitors.SingleAsync(TestContext.Current.CancellationToken);
            monitor.Resume(Noon.AddMinutes(2));
            await resumeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        ScheduledHttpCheckLease lease;
        await using (var claimContext = AnInstance.ContextFor(connectionString))
        {
            lease = Assert.IsType<ScheduledHttpCheckLease>(await new ScheduledHttpCheckStore(claimContext).ClaimAsync(
                Noon.AddMinutes(2),
                LeaseDuration,
                TestContext.Current.CancellationToken));
        }

        await using (var completionContext = AnInstance.ContextFor(connectionString))
        {
            await new ScheduledHttpCheckStore(completionContext).CompleteAsync(
                lease.CheckId,
                lease.Token,
                Success(),
                Noon.AddMinutes(2).AddSeconds(1),
                TestContext.Current.CancellationToken);
        }

        await using (var removalContext = AnInstance.ContextFor(connectionString))
        {
            var monitor = await removalContext.HttpMonitors.SingleAsync(TestContext.Current.CancellationToken);
            monitor.Remove(Noon.AddMinutes(3));
            await removalContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var removedContext = AnInstance.ContextFor(connectionString);
        Assert.Null(await new ScheduledHttpCheckStore(removedContext).ClaimAsync(
            Noon.AddHours(1),
            LeaseDuration,
            TestContext.Current.CancellationToken));
    }

    private async Task<string> EstablishedAsync(bool paused = false)
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = AnInstance.ContextFor(connectionString);
        await AnInstance.MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);
        var project = Project.Create("public-services", "Public services", Noon);
        var monitor = HttpMonitor.Create(
            project.Id,
            "public-site",
            "Public site",
            "https://status.example.test/health?token=target-secret",
            200,
            TextCondition.Required,
            "ready",
            300,
            10,
            3,
            null,
            null,
            Noon);
        if (paused)
        {
            monitor.Pause(Noon);
        }

        var secret = HttpMonitorSecret.FromTarget(
            monitor.Id,
            "https://status.example.test/health?token=target-secret");
        var (header, headerSecret) = HttpMonitorHeader.Create(
            monitor.Id,
            "Authorization",
            "Bearer header-secret",
            Noon);
        context.AddRange(project, monitor, secret, header, headerSecret);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return connectionString;
    }

    private static HttpExecutionResult Success() =>
        new(true, null, "The check succeeded.", 200, 25, "https://status.example.test/health");
}
