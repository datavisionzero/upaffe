using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Projects;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class PushHistoryRetentionPersistenceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Cutoff = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Pruning_observes_the_exclusive_boundary_and_preserves_current_and_open_incident_facts()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        Guid ordinaryOldId;
        Guid ordinaryBoundaryId;
        Guid ordinaryCurrentId;
        Guid openMissingId;
        Guid openLatestId;
        Guid resolvedFailureId;
        Guid resolvedCurrentId;
        Guid boundaryFailureId;
        Guid boundaryResolutionId;
        DateTimeOffset resolvedDeadline;
        await using (var setup = AnInstance.ContextFor(connectionString))
        {
            await AnInstance.MigratorFor(setup).ApplyAsync(TestContext.Current.CancellationToken);
            var createdAt = Cutoff.AddDays(-100);
            var project = Project.Create("backups", "Backups", createdAt);
            var ordinary = Monitor(project.Id, "ordinary", createdAt);
            var open = Monitor(project.Id, "open-incident", createdAt);
            var resolved = Monitor(project.Id, "resolved-incident", createdAt);
            var boundary = Monitor(project.Id, "boundary-incident", createdAt);
            setup.AddRange(project, ordinary, open, resolved, boundary);
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

            var ordinaryOld = Success(ordinary, Cutoff.AddSeconds(-1));
            setup.Add(ordinaryOld);
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
            var ordinaryBoundary = Success(ordinary, Cutoff);
            setup.Add(ordinaryBoundary);
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
            var ordinaryCurrent = Success(ordinary, Cutoff.AddSeconds(1));
            setup.Add(ordinaryCurrent);
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

            var missing = open.MissDeadline(Cutoff.AddMinutes(-3));
            Assert.True(open.ApplyReport(missing));
            var openIncident = PushIncident.Open(missing, missing.ReceivedAt);
            setup.AddRange(missing, openIncident);
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
            var openLatest = Failure(open, Cutoff.AddMinutes(-2));
            openIncident.ObserveFailure(openLatest);
            setup.Add(openLatest);
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

            var resolvedFailure = Failure(resolved, Cutoff.AddMinutes(-3));
            var resolvedIncident = PushIncident.Open(resolvedFailure, resolvedFailure.ReceivedAt);
            setup.AddRange(resolvedFailure, resolvedIncident);
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
            var resolvedCurrent = Success(resolved, Cutoff.AddMinutes(-2));
            resolvedIncident.Resolve(resolvedCurrent);
            setup.Add(resolvedCurrent);
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

            var boundaryFailure = Failure(boundary, Cutoff.AddMinutes(-1));
            var boundaryIncident = PushIncident.Open(boundaryFailure, boundaryFailure.ReceivedAt);
            setup.AddRange(boundaryFailure, boundaryIncident);
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
            var boundaryResolution = Success(boundary, Cutoff);
            boundaryIncident.Resolve(boundaryResolution);
            setup.Add(boundaryResolution);
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

            ordinaryOldId = ordinaryOld.Id;
            ordinaryBoundaryId = ordinaryBoundary.Id;
            ordinaryCurrentId = ordinaryCurrent.Id;
            openMissingId = missing.Id;
            openLatestId = openLatest.Id;
            resolvedFailureId = resolvedFailure.Id;
            resolvedCurrentId = resolvedCurrent.Id;
            boundaryFailureId = boundaryFailure.Id;
            boundaryResolutionId = boundaryResolution.Id;
            resolvedDeadline = resolved.NextDeadlineAt!.Value;
        }

        PushHistoryPruneResult result;
        await using (var prune = AnInstance.ContextFor(connectionString))
        {
            result = await new PushMonitorHistoryStore(prune).PruneAsync(
                Cutoff,
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, result.IncidentsDeleted);
        Assert.Equal(2, result.ReportsDeleted);
        await using var inspection = AnInstance.ContextFor(connectionString);
        var reportIds = await inspection.PushReports.AsNoTracking()
            .Select(value => value.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(ordinaryOldId, reportIds);
        Assert.DoesNotContain(resolvedFailureId, reportIds);
        Assert.Contains(ordinaryBoundaryId, reportIds);
        Assert.Contains(ordinaryCurrentId, reportIds);
        Assert.Contains(openMissingId, reportIds);
        Assert.Contains(openLatestId, reportIds);
        Assert.Contains(resolvedCurrentId, reportIds);
        Assert.Contains(boundaryFailureId, reportIds);
        Assert.Contains(boundaryResolutionId, reportIds);

        var incident = await inspection.PushIncidents.SingleAsync(value => value.ResolvedAt == null,
            TestContext.Current.CancellationToken);
        Assert.True(incident.IsOpen);
        Assert.Equal(openMissingId, incident.OpeningReportId);
        Assert.Equal(openLatestId, incident.LatestFailureReportId);
        Assert.Equal("report_missing", incident.OriginalReason);
        Assert.Equal("reported_failure", incident.LatestReason);

        var resolvedMonitor = await inspection.PushMonitors.SingleAsync(
            value => value.Key == "resolved-incident",
            TestContext.Current.CancellationToken);
        Assert.Equal(MonitorState.Healthy, resolvedMonitor.State);
        Assert.Equal(resolvedCurrentId, resolvedMonitor.LatestReportId);
        Assert.Equal(resolvedCurrentId, resolvedMonitor.LatestSuccessId);
        Assert.Equal(resolvedDeadline, resolvedMonitor.NextDeadlineAt);
        Assert.Equal(2, await inspection.PushIncidents.CountAsync(TestContext.Current.CancellationToken));

        await using var resumed = AnInstance.ContextFor(connectionString);
        Assert.Equal(new PushHistoryPruneResult(0, 0), await new PushMonitorHistoryStore(resumed)
            .PruneAsync(Cutoff, TestContext.Current.CancellationToken));
    }

    private static PushMonitor Monitor(Guid projectId, string key, DateTimeOffset createdAt) =>
        PushMonitor.Create(projectId, key, key, PushMonitorMode.JobCompletion, 60, 30, null, null, createdAt);

    private static PushReport Success(PushMonitor monitor, DateTimeOffset at)
    {
        var report = monitor.Receive(Guid.NewGuid(), at, at, ReportOutcome.Success, null);
        Assert.True(monitor.ApplyReport(report));
        return report;
    }

    private static PushReport Failure(PushMonitor monitor, DateTimeOffset at)
    {
        var report = monitor.Receive(Guid.NewGuid(), at, at, ReportOutcome.Failure, "sender detail");
        Assert.True(monitor.ApplyReport(report));
        return report;
    }
}
