using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Projects;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class HttpHistoryRetentionPersistenceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Cutoff = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Pruning_observes_the_exclusive_boundary_and_preserves_current_and_open_incident_facts()
    {
        var connectionString = await EstablishedAsync();

        var ordinaryOld = await RunAsync(
            connectionString,
            "ordinary",
            Failure("timeout"),
            Cutoff.AddSeconds(-1));
        var ordinaryBoundary = await RunAsync(
            connectionString,
            "ordinary",
            Success(),
            Cutoff);
        var ordinaryCurrent = await RunAsync(
            connectionString,
            "ordinary",
            Failure("unexpected_status"),
            Cutoff.AddSeconds(1));

        var openFirst = await RunAsync(
            connectionString,
            "open-incident",
            Failure("timeout"),
            Cutoff.AddMinutes(-3));
        var openAtThreshold = await RunAsync(
            connectionString,
            "open-incident",
            Failure("unexpected_status"),
            Cutoff.AddMinutes(-2));
        var openLatest = await RunAsync(
            connectionString,
            "open-incident",
            Failure("text_missing"),
            Cutoff.AddMinutes(-1));

        var resolvedFailure = await RunAsync(
            connectionString,
            "resolved-incident",
            Failure("tls_failure"),
            Cutoff.AddMinutes(-2));
        var resolvedCurrent = await RunAsync(
            connectionString,
            "resolved-incident",
            Success(),
            Cutoff.AddMinutes(-1));

        HttpHistoryPruneResult result;
        await using (var pruneContext = AnInstance.ContextFor(connectionString))
        {
            result = await new HttpMonitorHistoryStore(pruneContext).PruneAsync(
                Cutoff,
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, result.IncidentsDeleted);
        Assert.Equal(2, result.ChecksDeleted);
        await using var inspection = AnInstance.ContextFor(connectionString);
        var retainedCheckIds = await inspection.HttpChecks.AsNoTracking()
            .Select(value => value.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(ordinaryOld.CheckId, retainedCheckIds);
        Assert.DoesNotContain(resolvedFailure.CheckId, retainedCheckIds);
        Assert.Contains(ordinaryBoundary.CheckId, retainedCheckIds);
        Assert.Contains(ordinaryCurrent.CheckId, retainedCheckIds);
        Assert.Contains(openFirst.CheckId, retainedCheckIds);
        Assert.Contains(openAtThreshold.CheckId, retainedCheckIds);
        Assert.Contains(openLatest.CheckId, retainedCheckIds);
        Assert.Contains(resolvedCurrent.CheckId, retainedCheckIds);

        var incident = await inspection.Incidents.SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(incident.IsOpen);
        Assert.Equal(openFirst.CheckId, incident.FirstFailureCheckId);
        Assert.Equal(openAtThreshold.CheckId, incident.OpeningCheckId);
        Assert.Equal(openLatest.CheckId, incident.LatestFailureCheckId);

        var resolvedMonitor = await inspection.HttpMonitors.SingleAsync(
            value => value.Key == "resolved-incident",
            TestContext.Current.CancellationToken);
        Assert.Equal(MonitorState.Healthy, resolvedMonitor.State);
        Assert.Equal(resolvedCurrent.CheckId, resolvedMonitor.LatestResultId);
        Assert.Equal(resolvedCurrent.CheckId, resolvedMonitor.LatestSuccessId);
    }

    private async Task<string> EstablishedAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var context = AnInstance.ContextFor(connectionString);
        await AnInstance.MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);
        var createdAt = Cutoff.AddDays(-100);
        var project = Project.Create("public-services", "Public services", createdAt);
        var ordinary = Monitor(project.Id, "ordinary", failureThreshold: 100, createdAt);
        var open = Monitor(project.Id, "open-incident", failureThreshold: 2, createdAt);
        var resolved = Monitor(project.Id, "resolved-incident", failureThreshold: 1, createdAt);
        context.AddRange(
            project,
            ordinary,
            open,
            resolved,
            HttpMonitorSecret.FromTarget(ordinary.Id, ordinary.TargetUrl),
            HttpMonitorSecret.FromTarget(open.Id, open.TargetUrl),
            HttpMonitorSecret.FromTarget(resolved.Id, resolved.TargetUrl));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return connectionString;
    }

    private static HttpMonitor Monitor(Guid projectId, string key, int failureThreshold, DateTimeOffset now) =>
        HttpMonitor.Create(
            projectId,
            key,
            key,
            $"https://{key}.example.test/health",
            200,
            TextCondition.None,
            null,
            300,
            10,
            failureThreshold,
            null,
            null,
            now);

    private static async Task<CompletedHttpTest> RunAsync(
        string connectionString,
        string monitorKey,
        HttpExecutionResult result,
        DateTimeOffset now)
    {
        Guid checkId;
        await using (var startContext = AnInstance.ContextFor(connectionString))
        {
            var started = await new HttpMonitorStore(startContext).StartTestAsync(
                "public-services",
                monitorKey,
                now,
                TestContext.Current.CancellationToken);
            checkId = started.CheckId ?? throw new InvalidOperationException("The test check did not start.");
        }

        await using var completionContext = AnInstance.ContextFor(connectionString);
        return await new HttpMonitorStore(completionContext).CompleteTestAsync(
            checkId,
            result,
            now,
            TestContext.Current.CancellationToken);
    }

    private static HttpExecutionResult Failure(string reason) =>
        new(false, reason, "The check failed.", 503, 25, "https://status.example.test/health");

    private static HttpExecutionResult Success() =>
        new(true, null, "The check succeeded.", 200, 20, "https://status.example.test/health");
}
