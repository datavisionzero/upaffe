using Microsoft.EntityFrameworkCore;
using Npgsql;
using Upaffe.Domain.Access;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Projects;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class PushMonitoringPersistenceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Configuration_deadline_reports_and_secret_digest_survive_a_restart()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        Guid monitorId;
        Guid reportId;
        string issuedValue;

        await using (var first = AnInstance.ContextFor(connectionString))
        {
            await AnInstance.MigratorFor(first).ApplyAsync(TestContext.Current.CancellationToken);
            var project = Project.Create("backups", "Backups", Noon);
            var monitor = NewMonitor(project.Id);
            var credential = ReportingCredential.Create(monitor.Id, Noon);
            var issued = ReportingCredentialSecret.Issue(credential.Id, Noon);
            first.AddRange(project, monitor, credential, issued.Secret);
            await first.SaveChangesAsync(TestContext.Current.CancellationToken);
            var report = monitor.Receive(
                Guid.NewGuid(),
                Noon.AddMinutes(1),
                Noon.AddMinutes(1).AddSeconds(2),
                ReportOutcome.Success,
                null);
            first.Add(report);
            await first.SaveChangesAsync(TestContext.Current.CancellationToken);
            monitorId = monitor.Id;
            reportId = report.ReportId;
            issuedValue = issued.Value;
        }

        await using var restarted = AnInstance.ContextFor(connectionString);
        var stored = await restarted.PushMonitors.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        var storedReport = await restarted.PushReports.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        var storedSecret = await restarted.ReportingCredentialSecrets.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(monitorId, stored.Id);
        Assert.Equal(Noon.AddHours(25), stored.NextDeadlineAt);
        Assert.Equal(reportId, storedReport.ReportId);
        Assert.Equal(1, storedReport.Sequence);
        Assert.Equal(Noon.AddMinutes(1).AddSeconds(2), storedReport.ReceivedAt);
        Assert.Equal(SecretValue.Hash(issuedValue), storedSecret.SecretHash);
        Assert.StartsWith("uar_", issuedValue, StringComparison.Ordinal);
        Assert.Equal(47, issuedValue.Length);
    }

    [Fact]
    public async Task Credential_rotation_keeps_one_current_digest_and_revocation_is_durable()
    {
        await using var context = await MigratedContextAsync();
        var project = Project.Create("backups", "Backups", Noon);
        var monitor = NewMonitor(project.Id);
        var credential = ReportingCredential.Create(monitor.Id, Noon);
        var original = ReportingCredentialSecret.Issue(credential.Id, Noon);
        context.AddRange(project, monitor, credential, original.Secret);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var rotatedAt = Noon.AddMinutes(2);
        original.Secret.ExpireAt(rotatedAt.Add(ReportingCredentialSecret.RotationOverlap));
        credential.RecordRotation(rotatedAt);
        monitor.RecordCredentialChange(rotatedAt);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var replacement = ReportingCredentialSecret.Issue(credential.Id, rotatedAt);
        context.Add(replacement.Secret);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        credential.Revoke(Noon.AddMinutes(3));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var storedCredential = await context.ReportingCredentials.SingleAsync(TestContext.Current.CancellationToken);
        var secrets = await context.ReportingCredentialSecrets.OrderBy(value => value.CreatedAt)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(rotatedAt, storedCredential.RotatedAt);
        Assert.Equal(Noon.AddMinutes(3), storedCredential.RevokedAt);
        Assert.Equal(2, secrets.Count);
        Assert.Equal(rotatedAt.AddMinutes(5), secrets[0].ExpiresAt);
        Assert.Null(secrets[1].ExpiresAt);
        Assert.NotEqual(original.Value, replacement.Value);
    }

    [Fact]
    public async Task PostgreSQL_rejects_duplicate_report_identity_and_monitor_sequence()
    {
        await using var context = await MigratedContextAsync();
        var (monitor, _) = await AddMonitorAsync(context);
        var reportId = Guid.NewGuid();
        var first = PushReport.Receive(
            monitor.Id,
            reportId,
            1,
            1,
            Noon,
            Noon,
            ReportOutcome.Success,
            null);
        context.Add(first);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repeatedIdentity = PushReport.Receive(
            monitor.Id,
            reportId,
            1,
            2,
            Noon.AddSeconds(1),
            Noon.AddSeconds(1),
            ReportOutcome.Failure,
            "different");
        context.Add(repeatedIdentity);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(TestContext.Current.CancellationToken));
        context.Entry(repeatedIdentity).State = EntityState.Detached;

        var repeatedSequence = PushReport.Receive(
            monitor.Id,
            Guid.NewGuid(),
            1,
            1,
            Noon.AddSeconds(2),
            Noon.AddSeconds(2),
            ReportOutcome.Success,
            null);
        context.Add(repeatedSequence);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Concurrent_writers_cannot_persist_the_same_report_twice()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        Guid monitorId;
        await using (var setup = AnInstance.ContextFor(connectionString))
        {
            await AnInstance.MigratorFor(setup).ApplyAsync(TestContext.Current.CancellationToken);
            var added = await AddMonitorAsync(setup);
            monitorId = added.Monitor.Id;
        }

        var reportId = Guid.NewGuid();
        await using var one = AnInstance.ContextFor(connectionString);
        await using var two = AnInstance.ContextFor(connectionString);
        one.Add(PushReport.Receive(monitorId, reportId, 1, 1, Noon, Noon, ReportOutcome.Success, null));
        two.Add(PushReport.Receive(monitorId, reportId, 1, 1, Noon, Noon, ReportOutcome.Success, null));

        var attempts = await Task.WhenAll(SaveAsync(one), SaveAsync(two));
        Assert.Equal(1, attempts.Count(value => value));
        await using var verify = AnInstance.ContextFor(connectionString);
        Assert.Equal(1, await verify.PushReports.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PostgreSQL_protects_deadlines_values_and_one_open_incident()
    {
        await using var context = await MigratedContextAsync();
        var (monitor, _) = await AddMonitorAsync(context);

        var invalidTolerance = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlAsync(
                $"update push_monitor set tolerance_seconds = interval_seconds + 1 where id = {monitor.Id}",
                TestContext.Current.CancellationToken));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalidTolerance.SqlState);

        var failure = monitor.Receive(
            Guid.NewGuid(),
            Noon.AddMinutes(1),
            Noon.AddMinutes(1),
            ReportOutcome.Failure,
            "backup command failed");
        context.Add(failure);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var incident = PushIncident.Open(failure, Noon.AddMinutes(1));
        context.Add(incident);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var duplicate = PushIncident.Open(failure, Noon.AddMinutes(1));
        context.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<bool> SaveAsync(UpaffeDbContext context)
    {
        try
        {
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private async Task<(PushMonitor Monitor, ReportingCredential Credential)> AddMonitorAsync(UpaffeDbContext context)
    {
        var project = Project.Create("backups", "Backups", Noon);
        var monitor = NewMonitor(project.Id);
        var credential = ReportingCredential.Create(monitor.Id, Noon);
        var issued = ReportingCredentialSecret.Issue(credential.Id, Noon);
        context.AddRange(project, monitor, credential, issued.Secret);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (monitor, credential);
    }

    private static PushMonitor NewMonitor(Guid projectId) => PushMonitor.Create(
        projectId,
        "nightly-backup",
        "Nightly backup",
        PushMonitorMode.JobCompletion,
        24 * 60 * 60,
        60 * 60,
        "Inspect the backup logs.",
        "https://runbooks.example.test/nightly-backup",
        Noon);

    private async Task<UpaffeDbContext> MigratedContextAsync()
    {
        var context = AnInstance.ContextFor(await postgres.CreateDatabaseAsync());
        await AnInstance.MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);
        return context;
    }
}
