using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Projects;

namespace Upaffe.Infrastructure.Persistence;

public sealed class HttpMonitorStore(UpaffeDbContext context) : IHttpMonitorStore
{
    public async Task<HttpMonitorMutationResult> CreateAsync(
        string projectKey,
        HttpMonitorDefinition definition,
        IReadOnlyList<HttpHeaderValue> headers,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var project = await context.Projects.SingleOrDefaultAsync(
            value => value.Key == projectKey,
            cancellationToken);
        if (project is null)
        {
            return new(HttpMonitorMutation.ProjectMissing);
        }

        if (project.DeletedAt is not null)
        {
            return new(HttpMonitorMutation.ProjectDeleted);
        }

        var monitor = Create(project.Id, definition, now);
        var targetSecret = HttpMonitorSecret.FromTarget(monitor.Id, definition.TargetUrl);
        var headerParts = headers.Select(value =>
            HttpMonitorHeader.Create(monitor.Id, value.Name, value.Value, now)).ToArray();
        context.AddRange(monitor, targetSecret);
        context.HttpMonitorHeaders.AddRange(headerParts.Select(value => value.Header));
        context.HttpMonitorHeaderSecrets.AddRange(headerParts.Select(value => value.Secret));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new(HttpMonitorMutation.Changed, await SnapshotAsync(monitor, projectKey, cancellationToken));
        }
        catch (DbUpdateException exception) when (IsMonitorKeyConflict(exception))
        {
            context.ChangeTracker.Clear();
            var existing = await FindAsync(project.Id, definition.Key, includeDeleted: true, cancellationToken);
            if (existing is null)
            {
                return new(HttpMonitorMutation.Conflict);
            }

            var same = existing.DeletedAt is null
                && await SameDefinitionAsync(existing, definition, headers, cancellationToken);
            return same
                ? new(HttpMonitorMutation.Unchanged, await SnapshotAsync(existing, projectKey, cancellationToken))
                : new(HttpMonitorMutation.Conflict);
        }
    }

    public async Task<HttpMonitorSnapshot?> GetAsync(
        string projectKey,
        string monitorKey,
        CancellationToken cancellationToken)
    {
        var found = await FindLiveAsync(projectKey, monitorKey, cancellationToken);
        return found is null
            ? null
            : await SnapshotAsync(found.Value.Monitor, projectKey, cancellationToken);
    }

    public async Task<IReadOnlyList<HttpMonitorSnapshot>?> ListAsync(
        string projectKey,
        CancellationToken cancellationToken)
    {
        var project = await context.Projects.AsNoTracking().SingleOrDefaultAsync(
            value => value.Key == projectKey && value.DeletedAt == null,
            cancellationToken);
        if (project is null)
        {
            return null;
        }

        var monitors = await context.HttpMonitors.AsNoTracking()
            .Where(value => value.ProjectId == project.Id && value.DeletedAt == null)
            .OrderBy(value => value.Key)
            .ToListAsync(cancellationToken);
        var result = new List<HttpMonitorSnapshot>(monitors.Count);
        foreach (var monitor in monitors)
        {
            result.Add(await SnapshotAsync(monitor, projectKey, cancellationToken));
        }

        return result;
    }

    public async Task<HttpMonitorMutationResult> UpdateAsync(
        string projectKey,
        string monitorKey,
        HttpMonitorChange change,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var found = await FindForChangeAsync(projectKey, monitorKey, expectedVersion, cancellationToken);
        if (found.Result is not null)
        {
            return found.Result;
        }

        var monitor = found.Monitor!;
        var targetSecret = await context.HttpMonitorSecrets.SingleAsync(
            value => value.MonitorId == monitor.Id,
            cancellationToken);
        var target = change.TargetUrl ?? monitor.TargetUrl + targetSecret.RevealTargetQuery();
        monitor.ChangeConfiguration(
            change.Name,
            target,
            change.ExpectedStatusCode,
            change.TextCondition,
            change.TextFragment,
            change.IntervalSeconds,
            change.TimeoutSeconds,
            change.FailureThreshold,
            change.Instruction,
            change.RunbookUrl,
            now);
        if (change.TargetUrl is not null)
        {
            targetSecret.ReplaceTarget(change.TargetUrl);
        }

        return await SaveAsync(monitor, projectKey, cancellationToken);
    }

    public Task<HttpMonitorMutationResult> PauseAsync(
        string projectKey,
        string monitorKey,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ChangeStateAsync(projectKey, monitorKey, expectedVersion, now, pause: true, cancellationToken);

    public Task<HttpMonitorMutationResult> ResumeAsync(
        string projectKey,
        string monitorKey,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        ChangeStateAsync(projectKey, monitorKey, expectedVersion, now, pause: false, cancellationToken);

    public async Task<HttpMonitorMutationResult> RemoveAsync(
        string projectKey,
        string monitorKey,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var project = await context.Projects.SingleOrDefaultAsync(value => value.Key == projectKey, cancellationToken);
        if (project is null)
        {
            return new(HttpMonitorMutation.ProjectMissing);
        }

        var monitor = await FindAsync(project.Id, monitorKey, includeDeleted: true, cancellationToken);
        if (monitor is null)
        {
            return new(HttpMonitorMutation.Missing);
        }

        if (monitor.Version != expectedVersion)
        {
            return new(HttpMonitorMutation.VersionConflict);
        }

        if (monitor.DeletedAt is not null)
        {
            return new(HttpMonitorMutation.Unchanged, await SnapshotAsync(monitor, projectKey, cancellationToken));
        }

        monitor.Remove(now);
        return await SaveAsync(monitor, projectKey, cancellationToken);
    }

    public async Task<HttpMonitorMutationResult> SetHeaderAsync(
        string projectKey,
        string monitorKey,
        string name,
        string value,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var found = await FindForChangeAsync(projectKey, monitorKey, expectedVersion, cancellationToken);
        if (found.Result is not null)
        {
            return found.Result;
        }

        var monitor = found.Monitor!;
        var header = await context.HttpMonitorHeaders.SingleOrDefaultAsync(
            item => item.MonitorId == monitor.Id && item.Name == name,
            cancellationToken);
        if (header is null)
        {
            if (await context.HttpMonitorHeaders.CountAsync(item => item.MonitorId == monitor.Id, cancellationToken) >= 32)
            {
                return new(HttpMonitorMutation.Conflict);
            }

            var created = HttpMonitorHeader.Create(monitor.Id, name, value, now);
            context.AddRange(created.Header, created.Secret);
        }
        else
        {
            var secret = await context.HttpMonitorHeaderSecrets.SingleAsync(
                item => item.HeaderId == header.Id,
                cancellationToken);
            if (secret.Reveal() == value)
            {
                return new(HttpMonitorMutation.Unchanged, await SnapshotAsync(monitor, projectKey, cancellationToken));
            }

            secret.Replace(value);
            header.Touch(now);
        }

        var storedBytes = await context.HttpMonitorHeaders
            .Where(item => item.MonitorId == monitor.Id && item.Name != name)
            .Join(
                context.HttpMonitorHeaderSecrets,
                header => header.Id,
                secret => secret.HeaderId,
                (header, secret) => header.Name.Length + secret.ValueUtf8.Length)
            .SumAsync(cancellationToken);
        if (storedBytes + Encoding.ASCII.GetByteCount(name) + Encoding.UTF8.GetByteCount(value) > 16 * 1_024)
        {
            context.ChangeTracker.Clear();
            return new(HttpMonitorMutation.Conflict);
        }

        monitor.RecordSecretChange(now);
        return await SaveAsync(monitor, projectKey, cancellationToken);
    }

    public async Task<HttpMonitorMutationResult> RemoveHeaderAsync(
        string projectKey,
        string monitorKey,
        string name,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var found = await FindForChangeAsync(projectKey, monitorKey, expectedVersion, cancellationToken);
        if (found.Result is not null)
        {
            return found.Result;
        }

        var monitor = found.Monitor!;
        var header = await context.HttpMonitorHeaders.SingleOrDefaultAsync(
            item => item.MonitorId == monitor.Id && item.Name == name,
            cancellationToken);
        if (header is null)
        {
            return new(HttpMonitorMutation.Unchanged, await SnapshotAsync(monitor, projectKey, cancellationToken));
        }

        context.HttpMonitorHeaders.Remove(header);
        monitor.RecordSecretChange(now);
        return await SaveAsync(monitor, projectKey, cancellationToken);
    }

    public async Task<StartedHttpTest> StartTestAsync(
        string projectKey,
        string monitorKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var projectId = await context.Projects.AsNoTracking()
            .Where(value => value.Key == projectKey && value.DeletedAt == null)
            .Select(value => (Guid?)value.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (projectId is null)
        {
            return new(HttpMonitorMutation.Missing);
        }

        var monitorId = await context.HttpMonitors.AsNoTracking()
            .Where(value => value.ProjectId == projectId && value.Key == monitorKey && value.DeletedAt == null)
            .Select(value => (Guid?)value.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (monitorId is null
            || !await PostgresRowLocks.HttpMonitorAsync(context, monitorId.Value, transaction, cancellationToken))
        {
            return new(HttpMonitorMutation.Missing);
        }

        // Load after acquiring the same row lock used by the scheduler so the
        // sequence is allocated from the latest committed monitor state.
        var monitor = await context.HttpMonitors.SingleOrDefaultAsync(
            value => value.Id == monitorId && value.DeletedAt == null,
            cancellationToken);
        if (monitor is null)
        {
            return new(HttpMonitorMutation.Missing);
        }

        if (monitor.State == MonitorState.Paused)
        {
            return new(HttpMonitorMutation.Paused);
        }

        var request = await ExecutionRequestAsync(monitor, cancellationToken);
        var check = monitor.BeginCheck(CheckTrigger.Requested, now, now);
        context.HttpChecks.Add(check);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(HttpMonitorMutation.Changed, check.Id, request);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(HttpMonitorMutation.VersionConflict);
        }
    }

    public async Task<CompletedHttpTest> CompleteTestAsync(
        Guid checkId,
        HttpExecutionResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (!await PostgresRowLocks.HttpCheckAsync(context, checkId, transaction, cancellationToken))
        {
            throw new InvalidOperationException("The requested HTTP check no longer exists.");
        }

        var check = await context.HttpChecks.SingleAsync(value => value.Id == checkId, cancellationToken);
        if (!await PostgresRowLocks.HttpMonitorAsync(context, check.MonitorId, transaction, cancellationToken))
        {
            throw new InvalidOperationException("The requested HTTP monitor no longer exists.");
        }

        var monitor = await context.HttpMonitors.SingleAsync(value => value.Id == check.MonitorId, cancellationToken);
        var projectKey = await context.Projects.Where(value => value.Id == monitor.ProjectId)
            .Select(value => value.Key)
            .SingleAsync(cancellationToken);
        var alreadyCompleted = check.IsCompleted;
        if (!alreadyCompleted)
        {
            if (result.Succeeded)
            {
                check.CompleteSuccess(
                    now,
                    result.StatusCode ?? throw new InvalidOperationException("A successful execution needs a status."),
                    result.ResponseTimeMilliseconds,
                    result.EffectiveUrl ?? throw new InvalidOperationException("A successful execution needs an effective URL."));
            }
            else
            {
                check.CompleteFailure(
                    result.ReasonCode ?? "execution_failed",
                    now,
                    result.StatusCode,
                    result.ResponseTimeMilliseconds,
                    result.EffectiveUrl);
            }
        }

        var applied = !alreadyCompleted
            && await HttpCheckEvaluator.ApplyAsync(context, monitor, check, now, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(check.Id, applied, result, await SnapshotAsync(monitor, projectKey, cancellationToken));
    }

    private async Task<HttpMonitorMutationResult> ChangeStateAsync(
        string projectKey,
        string monitorKey,
        long expectedVersion,
        DateTimeOffset now,
        bool pause,
        CancellationToken cancellationToken)
    {
        var found = await FindForChangeAsync(projectKey, monitorKey, expectedVersion, cancellationToken);
        if (found.Result is not null)
        {
            return found.Result;
        }

        var monitor = found.Monitor!;
        if ((pause && monitor.State == MonitorState.Paused)
            || (!pause && monitor.State != MonitorState.Paused))
        {
            return new(HttpMonitorMutation.Unchanged, await SnapshotAsync(monitor, projectKey, cancellationToken));
        }

        if (pause)
        {
            monitor.Pause(now);
        }
        else
        {
            monitor.Resume(now);
        }

        return await SaveAsync(monitor, projectKey, cancellationToken);
    }

    private async Task<(HttpMonitor? Monitor, HttpMonitorMutationResult? Result)> FindForChangeAsync(
        string projectKey,
        string monitorKey,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        var project = await context.Projects.SingleOrDefaultAsync(value => value.Key == projectKey, cancellationToken);
        if (project is null)
        {
            return (null, new(HttpMonitorMutation.ProjectMissing));
        }

        if (project.DeletedAt is not null)
        {
            return (null, new(HttpMonitorMutation.ProjectDeleted));
        }

        var monitor = await FindAsync(project.Id, monitorKey, includeDeleted: true, cancellationToken);
        if (monitor is null)
        {
            return (null, new(HttpMonitorMutation.Missing));
        }

        if (monitor.Version != expectedVersion)
        {
            return (null, new(HttpMonitorMutation.VersionConflict));
        }

        if (monitor.DeletedAt is not null)
        {
            return (null, new(HttpMonitorMutation.Deleted));
        }

        return (monitor, null);
    }

    private async Task<(Project Project, HttpMonitor Monitor)?> FindLiveAsync(
        string projectKey,
        string monitorKey,
        CancellationToken cancellationToken)
    {
        var project = await context.Projects.AsNoTracking().SingleOrDefaultAsync(
            value => value.Key == projectKey && value.DeletedAt == null,
            cancellationToken);
        if (project is null)
        {
            return null;
        }

        var monitor = await FindAsync(project.Id, monitorKey, includeDeleted: false, cancellationToken);
        return monitor is null ? null : (project, monitor);
    }

    private Task<HttpMonitor?> FindAsync(
        Guid projectId,
        string monitorKey,
        bool includeDeleted,
        CancellationToken cancellationToken) =>
        context.HttpMonitors.SingleOrDefaultAsync(
            value => value.ProjectId == projectId
                && value.Key == monitorKey
                && (includeDeleted || value.DeletedAt == null),
            cancellationToken);

    private async Task<HttpMonitorMutationResult> SaveAsync(
        HttpMonitor monitor,
        string projectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new(HttpMonitorMutation.Changed, await SnapshotAsync(monitor, projectKey, cancellationToken));
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(HttpMonitorMutation.VersionConflict);
        }
    }

    private async Task<HttpMonitorSnapshot> SnapshotAsync(
        HttpMonitor monitor,
        string projectKey,
        CancellationToken cancellationToken)
    {
        var openIncidentId = await context.Incidents.AsNoTracking()
            .Where(value => value.MonitorId == monitor.Id && value.ResolvedAt == null)
            .Select(value => (Guid?)value.Id)
            .SingleOrDefaultAsync(cancellationToken);
        var headers = await context.HttpMonitorHeaders.AsNoTracking()
            .Where(value => value.MonitorId == monitor.Id)
            .OrderBy(value => value.Name)
            .Select(value => new HttpHeaderSnapshot(value.Id, value.Name, value.CreatedAt, value.UpdatedAt))
            .ToListAsync(cancellationToken);
        return new(
            monitor.Id,
            projectKey,
            monitor.Key,
            monitor.Name,
            monitor.TargetUrl,
            monitor.HasTargetQuery,
            monitor.ExpectedStatusCode,
            monitor.TextCondition,
            monitor.TextFragment,
            monitor.IntervalSeconds,
            monitor.TimeoutSeconds,
            monitor.FailureThreshold,
            monitor.Instruction,
            monitor.RunbookUrl,
            monitor.State,
            monitor.ConsecutiveFailures,
            monitor.NextCheckAt,
            monitor.LatestResultId,
            monitor.LatestSuccessId,
            openIncidentId,
            headers,
            monitor.Version,
            monitor.CreatedAt,
            monitor.UpdatedAt,
            monitor.PausedAt,
            monitor.DeletedAt);
    }

    private async Task<HttpExecutionRequest> ExecutionRequestAsync(
        HttpMonitor monitor,
        CancellationToken cancellationToken) =>
        await HttpExecutionRequestFactory.CreateAsync(context, monitor, cancellationToken);

    private async Task<bool> SameDefinitionAsync(
        HttpMonitor existing,
        HttpMonitorDefinition definition,
        IReadOnlyList<HttpHeaderValue> headers,
        CancellationToken cancellationToken)
    {
        var candidate = Create(existing.ProjectId, definition, existing.CreatedAt);
        if (existing.Name != candidate.Name
            || existing.TargetUrl != candidate.TargetUrl
            || existing.HasTargetQuery != candidate.HasTargetQuery
            || existing.ExpectedStatusCode != candidate.ExpectedStatusCode
            || existing.TextCondition != candidate.TextCondition
            || existing.TextFragment != candidate.TextFragment
            || existing.IntervalSeconds != candidate.IntervalSeconds
            || existing.TimeoutSeconds != candidate.TimeoutSeconds
            || existing.FailureThreshold != candidate.FailureThreshold
            || existing.Instruction != candidate.Instruction
            || existing.RunbookUrl != candidate.RunbookUrl)
        {
            return false;
        }

        var secret = await context.HttpMonitorSecrets.AsNoTracking().SingleAsync(
            value => value.MonitorId == existing.Id,
            cancellationToken);
        var candidateSecret = HttpMonitorSecret.FromTarget(candidate.Id, definition.TargetUrl);
        if (!(secret.TargetQueryUtf8 ?? []).SequenceEqual(candidateSecret.TargetQueryUtf8 ?? []))
        {
            return false;
        }

        var storedHeaders = await ExecutionRequestAsync(existing, cancellationToken);
        return storedHeaders.Headers.OrderBy(value => value.Name, StringComparer.Ordinal)
            .SequenceEqual(
                headers.OrderBy(value => value.Name, StringComparer.Ordinal)
                    .Select(value => new HttpExecutionHeader(value.Name, value.Value)));
    }

    private static HttpMonitor Create(Guid projectId, HttpMonitorDefinition value, DateTimeOffset now) =>
        HttpMonitor.Create(
            projectId,
            value.Key,
            value.Name,
            value.TargetUrl,
            value.ExpectedStatusCode,
            value.TextCondition,
            value.TextFragment,
            value.IntervalSeconds,
            value.TimeoutSeconds,
            value.FailureThreshold,
            value.Instruction,
            value.RunbookUrl,
            now);

    private static bool IsMonitorKeyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "http_monitor_project_key",
        };
}
