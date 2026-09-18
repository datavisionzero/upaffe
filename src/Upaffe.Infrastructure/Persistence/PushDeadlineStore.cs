using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;

namespace Upaffe.Infrastructure.Persistence;

public sealed class PushDeadlineStore(UpaffeDbContext context) : IPushDeadlineStore
{
    public async Task<PushDeadlineLease?> ClaimAsync(
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var monitorId = await SelectDueMonitorAsync(now, transaction, cancellationToken);
        if (monitorId is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var monitor = await context.PushMonitors.SingleAsync(value => value.Id == monitorId, cancellationToken);
        var token = Guid.NewGuid();
        monitor.ClaimDeadline(token, now, now.Add(leaseDuration));
        var lease = new PushDeadlineLease(
            monitor.Id,
            token,
            monitor.EvaluationGeneration,
            monitor.NextDeadlineAt ?? throw new InvalidOperationException("A claimed monitor needs a deadline."));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        context.ChangeTracker.Clear();
        return lease;
    }

    public async Task<PushDeadlineCompletion> CompleteAsync(
        PushDeadlineLease lease,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (!await PostgresRowLocks.PushMonitorAsync(context, lease.MonitorId, transaction, cancellationToken))
        {
            throw new InvalidOperationException("The push monitor no longer exists.");
        }

        var monitor = await context.PushMonitors.SingleAsync(value => value.Id == lease.MonitorId, cancellationToken);
        if (!monitor.IsDeadlineClaimedBy(lease.Token))
        {
            await transaction.CommitAsync(cancellationToken);
            return PushDeadlineCompletion.LeaseLost;
        }

        if (monitor.DeletedAt is not null
            || monitor.State == MonitorState.Paused
            || monitor.EvaluationGeneration != lease.EvaluationGeneration
            || monitor.NextDeadlineAt != lease.DeadlineAt
            || now <= lease.DeadlineAt)
        {
            monitor.ClearDeadlineLease();
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return PushDeadlineCompletion.Superseded;
        }

        var report = monitor.MissDeadline(now);
        context.PushReports.Add(report);
        await PushReportEvaluator.ApplyAsync(context, monitor, report, cancellationToken);
        monitor.ClearDeadlineLease();
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PushDeadlineCompletion.Completed;
    }

    private async Task<Guid?> SelectDueMonitorAsync(
        DateTimeOffset now,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            """
            select m.id
            from push_monitor as m
            join project as p on p.id = m.project_id
            where m.deleted_at is null
              and m.state <> 'Paused'
              and m.next_deadline_at < @now
              and (m.deadline_lease_until is null or m.deadline_lease_until <= @now)
              and p.deleted_at is null
              and not exists (
                  select 1
                  from push_report as r
                  where r.monitor_id = m.id
                    and r.evaluation_generation = m.evaluation_generation
                    and r.observed_at = m.next_deadline_at
                    and r.is_deadline_observation)
            order by m.next_deadline_at, m.id
            for update of m skip locked
            limit 1
            """;
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("now", now));
        return await command.ExecuteScalarAsync(cancellationToken) is Guid id ? id : null;
    }
}
