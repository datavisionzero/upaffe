using System.Data;
using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;
using Upaffe.Domain.Monitoring;

namespace Upaffe.Infrastructure.Persistence;

public sealed class PushReportStore(UpaffeDbContext context) : IPushReportStore
{
    public async Task<PushReportMutationResult> SubmitAsync(
        string token,
        PushReportSubmission submission,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        if (!ValidToken(token))
        {
            return new(PushReportMutation.Rejected);
        }

        var hash = SecretValue.Hash(token);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var monitorId = await (
                from credential in context.ReportingCredentials.AsNoTracking()
                join secret in context.ReportingCredentialSecrets.AsNoTracking()
                    on credential.Id equals secret.CredentialId
                join storedMonitor in context.PushMonitors.AsNoTracking()
                    on credential.MonitorId equals storedMonitor.Id
                join project in context.Projects.AsNoTracking()
                    on storedMonitor.ProjectId equals project.Id
                where secret.SecretHash == hash
                    && credential.RevokedAt == null
                    && (secret.ExpiresAt == null || receivedAt < secret.ExpiresAt)
                    && storedMonitor.DeletedAt == null
                    && project.DeletedAt == null
                select (Guid?)storedMonitor.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (monitorId is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(PushReportMutation.Rejected);
        }

        var monitor = await context.PushMonitors
            .FromSqlInterpolated($"select * from push_monitor where id = {monitorId.Value} for update")
            .SingleAsync(cancellationToken);
        var existing = await context.PushReports.SingleOrDefaultAsync(
            value => value.MonitorId == monitor.Id && value.ReportId == submission.ReportId,
            cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return Same(existing, submission)
                ? new(PushReportMutation.Accepted, Receipt(existing, duplicate: true))
                : new(PushReportMutation.Conflict);
        }

        if (monitor.State == MonitorState.Paused)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(PushReportMutation.Paused);
        }

        var report = monitor.Receive(
            submission.ReportId,
            submission.ObservedAt,
            receivedAt,
            submission.Outcome,
            submission.DiagnosticReason);
        context.PushReports.Add(report);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(PushReportMutation.Accepted, Receipt(report, duplicate: false));
    }

    private static bool ValidToken(string value) =>
        value.Length == 47
        && value.StartsWith("uar_", StringComparison.Ordinal)
        && value.AsSpan(4).ToArray().All(character =>
            character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '-' or '_');

    private static bool Same(PushReport report, PushReportSubmission submission) =>
        report.ObservedAt == submission.ObservedAt
        && report.Outcome == submission.Outcome
        && report.DiagnosticReason == submission.DiagnosticReason
        && !report.IsDeadlineObservation;

    private static PushReportReceipt Receipt(PushReport report, bool duplicate) =>
        new(report.ReportId, report.ReceivedAt, report.Sequence, report.Applicable, duplicate);
}
