using System.Data;
using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;

namespace Upaffe.Infrastructure.Persistence;

public sealed class ReportingCredentialStore(UpaffeDbContext context) : IReportingCredentialStore
{
    public async Task<ReportingCredentialMetadata?> GetAsync(string projectKey, string monitorKey, CancellationToken cancellationToken) =>
        await Query(projectKey, monitorKey).Select(value => new ReportingCredentialMetadata(value.Id, value.CreatedAt, value.RotatedAt, value.RevokedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<ReportingCredentialMutationResult> IssueAsync(string projectKey, string monitorKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var monitor = await LiveMonitorAsync(projectKey, monitorKey, cancellationToken);
        if (monitor is null) return new(ReportingCredentialMutation.Missing);
        if (await context.ReportingCredentials.AnyAsync(value => value.MonitorId == monitor.Id, cancellationToken)) return new(ReportingCredentialMutation.Conflict);
        var credential = ReportingCredential.Create(monitor.Id, now);
        var issued = ReportingCredentialSecret.Issue(credential.Id, now);
        monitor.RecordCredentialChange(now);
        context.AddRange(credential, issued.Secret);
        await context.SaveChangesAsync(cancellationToken);
        return new(ReportingCredentialMutation.Changed, new(Metadata(credential), issued.Value));
    }

    public async Task<ReportingCredentialMutationResult> RotateAsync(string projectKey, string monitorKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var credential = await LockedAsync(projectKey, monitorKey, cancellationToken);
        if (credential is null) return new(ReportingCredentialMutation.Missing);
        if (credential.RevokedAt is not null) return new(ReportingCredentialMutation.Revoked);
        var current = await context.ReportingCredentialSecrets.SingleAsync(value => value.CredentialId == credential.Id && value.ExpiresAt == null, cancellationToken);
        var until = now.Add(ReportingCredentialSecret.RotationOverlap);
        current.ExpireAt(until);
        credential.RecordRotation(now);
        var issued = ReportingCredentialSecret.Issue(credential.Id, now);
        context.Add(issued.Secret);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(ReportingCredentialMutation.Changed, new(Metadata(credential), issued.Value, until));
    }

    public async Task<ReportingCredentialMutation> RevokeAsync(string projectKey, string monitorKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var credential = await LockedAsync(projectKey, monitorKey, cancellationToken);
        if (credential is null) return ReportingCredentialMutation.Missing;
        credential.Revoke(now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReportingCredentialMutation.Changed;
    }

    private IQueryable<ReportingCredential> Query(string projectKey, string monitorKey) =>
        from credential in context.ReportingCredentials.AsNoTracking()
        join monitor in context.PushMonitors.AsNoTracking() on credential.MonitorId equals monitor.Id
        join project in context.Projects.AsNoTracking() on monitor.ProjectId equals project.Id
        where project.Key == projectKey && project.DeletedAt == null && monitor.Key == monitorKey && monitor.DeletedAt == null
        select credential;

    private async Task<PushMonitor?> LiveMonitorAsync(string projectKey, string monitorKey, CancellationToken cancellationToken) =>
        await (from monitor in context.PushMonitors
               join project in context.Projects on monitor.ProjectId equals project.Id
               where project.Key == projectKey && project.DeletedAt == null && monitor.Key == monitorKey && monitor.DeletedAt == null
               select monitor).SingleOrDefaultAsync(cancellationToken);

    private Task<ReportingCredential?> LockedAsync(string projectKey, string monitorKey, CancellationToken cancellationToken) =>
        context.ReportingCredentials.FromSqlInterpolated($"select c.* from reporting_credential c join push_monitor m on m.id = c.monitor_id join project p on p.id = m.project_id where p.key = {projectKey} and p.deleted_at is null and m.key = {monitorKey} and m.deleted_at is null for update of c")
            .SingleOrDefaultAsync(cancellationToken);

    private static ReportingCredentialMetadata Metadata(ReportingCredential value) => new(value.Id, value.CreatedAt, value.RotatedAt, value.RevokedAt);
}
