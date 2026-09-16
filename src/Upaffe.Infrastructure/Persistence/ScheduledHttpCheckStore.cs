using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;

namespace Upaffe.Infrastructure.Persistence;

public sealed class ScheduledHttpCheckStore(UpaffeDbContext context) : IScheduledHttpCheckStore
{
    public async Task<ScheduledHttpCheckLease?> ClaimAsync(
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var checkId = await SelectIdAsync(
            """
            select c.id
            from http_check as c
            join http_monitor as m on m.id = c.monitor_id
            join project as p on p.id = m.project_id
            where c.trigger = 'Scheduled'
              and c.completed_at is null
              and c.execution_lease_until <= @now
              and m.deleted_at is null
              and m.state <> 'Paused'
              and m.evaluation_generation = c.evaluation_generation
              and p.deleted_at is null
            order by c.execution_lease_until, c.scheduled_for, c.id
            for update of c, m skip locked
            limit 1
            """,
            now,
            transaction,
            cancellationToken);

        HttpCheck check;
        HttpMonitor monitor;
        if (checkId is not null)
        {
            check = await context.HttpChecks.SingleAsync(value => value.Id == checkId, cancellationToken);
            monitor = await context.HttpMonitors.SingleAsync(value => value.Id == check.MonitorId, cancellationToken);
        }
        else
        {
            var monitorId = await SelectIdAsync(
                """
                select m.id
                from http_monitor as m
                join project as p on p.id = m.project_id
                where m.deleted_at is null
                  and m.state <> 'Paused'
                  and m.next_check_at <= @now
                  and p.deleted_at is null
                order by m.next_check_at, m.id
                for update of m skip locked
                limit 1
                """,
                now,
                transaction,
                cancellationToken);
            if (monitorId is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            monitor = await context.HttpMonitors.SingleAsync(value => value.Id == monitorId, cancellationToken);
            check = monitor.BeginCheck(
                CheckTrigger.Scheduled,
                monitor.NextCheckAt ?? throw new InvalidOperationException("A claimed monitor needs a due time."),
                now);
            context.HttpChecks.Add(check);
        }

        var token = Guid.NewGuid();
        check.ClaimExecution(token, now, now.Add(leaseDuration));
        var request = await HttpExecutionRequestFactory.CreateAsync(context, monitor, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(check.Id, token, request);
    }

    public async Task<ScheduledHttpCheckCompletion> CompleteAsync(
        Guid checkId,
        Guid leaseToken,
        HttpExecutionResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (!await PostgresRowLocks.HttpCheckAsync(context, checkId, transaction, cancellationToken))
        {
            throw new InvalidOperationException("The scheduled HTTP check no longer exists.");
        }

        var check = await context.HttpChecks.SingleAsync(value => value.Id == checkId, cancellationToken);
        if (check.IsCompleted)
        {
            await transaction.CommitAsync(cancellationToken);
            return ScheduledHttpCheckCompletion.AlreadyCompleted;
        }

        if (!check.IsClaimedBy(leaseToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return ScheduledHttpCheckCompletion.LeaseLost;
        }

        if (!await PostgresRowLocks.HttpMonitorAsync(context, check.MonitorId, transaction, cancellationToken))
        {
            throw new InvalidOperationException("The scheduled HTTP monitor no longer exists.");
        }

        var monitor = await context.HttpMonitors.SingleAsync(value => value.Id == check.MonitorId, cancellationToken);
        Complete(check, result, now);
        await HttpCheckEvaluator.ApplyAsync(context, monitor, check, now, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ScheduledHttpCheckCompletion.Completed;
    }

    private async Task<Guid?> SelectIdAsync(
        string sql,
        DateTimeOffset now,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = sql;
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset>("now", now));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid id ? id : null;
    }

    private static void Complete(HttpCheck check, HttpExecutionResult result, DateTimeOffset now)
    {
        if (result.Succeeded)
        {
            check.CompleteSuccess(
                now,
                result.StatusCode ?? throw new InvalidOperationException("A successful execution needs a status."),
                result.ResponseTimeMilliseconds,
                result.EffectiveUrl ?? throw new InvalidOperationException("A successful execution needs an effective URL."));
            return;
        }

        check.CompleteFailure(
            result.ReasonCode ?? "execution_failed",
            now,
            result.StatusCode,
            result.ResponseTimeMilliseconds,
            result.EffectiveUrl);
    }
}
