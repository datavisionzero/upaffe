using Microsoft.EntityFrameworkCore;
using Npgsql;
using Upaffe.Application.Ports;
using Upaffe.Domain.Projects;

namespace Upaffe.Infrastructure.Persistence;

/// <summary>Persists immutable project identity and optimistic project changes.</summary>
public sealed class ProjectStore(UpaffeDbContext context) : IProjectStore
{
    public async Task<ProjectMutationResult> CreateAsync(
        string key,
        string name,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var project = Project.Create(key, name, now);
        context.Projects.Add(project);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new(ProjectMutation.Changed, Snapshot(project));
        }
        catch (DbUpdateException exception) when (IsKeyConflict(exception))
        {
            context.Entry(project).State = EntityState.Detached;
            var existing = await context.Projects.AsNoTracking().SingleAsync(
                value => value.Key == key,
                cancellationToken);
            return existing.Name == name
                ? new(ProjectMutation.Unchanged, Snapshot(existing))
                : new(ProjectMutation.Conflict);
        }
    }

    public Task<ProjectSnapshot?> GetAsync(string key, CancellationToken cancellationToken) =>
        context.Projects.AsNoTracking()
            .Where(value => value.Key == key)
            .Select(value => Snapshot(value))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ProjectSnapshot>> ListAsync(
        bool deleted,
        CancellationToken cancellationToken) =>
        await context.Projects.AsNoTracking()
            .Where(value => deleted ? value.DeletedAt != null : value.DeletedAt == null)
            .OrderBy(value => value.Key)
            .Select(value => Snapshot(value))
            .ToListAsync(cancellationToken);

    public async Task<ProjectMutationResult> RenameAsync(
        string key,
        string name,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var project = await FindAsync(key, cancellationToken);
        if (project is null)
        {
            return new(ProjectMutation.Missing);
        }

        if (project.Version != expectedVersion)
        {
            return new(ProjectMutation.VersionConflict);
        }

        if (project.DeletedAt is not null)
        {
            return new(ProjectMutation.Deleted);
        }

        project.Rename(name, now);
        return await SavedAsync(project, cancellationToken);
    }

    public async Task<ProjectMutationResult> DeleteAsync(
        string key,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var project = await FindAsync(key, cancellationToken);
        if (project is null)
        {
            return new(ProjectMutation.Missing);
        }

        if (project.Version != expectedVersion)
        {
            return new(ProjectMutation.VersionConflict);
        }

        if (project.DeletedAt is not null)
        {
            return new(ProjectMutation.Unchanged, Snapshot(project));
        }

        project.Delete(now);
        return await SavedAsync(project, cancellationToken);
    }

    public async Task<ProjectMutationResult> RestoreAsync(
        string key,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var project = await FindAsync(key, cancellationToken);
        if (project is null)
        {
            return new(ProjectMutation.Missing);
        }

        if (project.Version != expectedVersion)
        {
            return new(ProjectMutation.VersionConflict);
        }

        if (project.DeletedAt is null)
        {
            return new(ProjectMutation.Unchanged, Snapshot(project));
        }

        project.Restore(now);
        return await SavedAsync(project, cancellationToken);
    }

    private Task<Project?> FindAsync(string key, CancellationToken cancellationToken) =>
        context.Projects.SingleOrDefaultAsync(value => value.Key == key, cancellationToken);

    private async Task<ProjectMutationResult> SavedAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new(ProjectMutation.Changed, Snapshot(project));
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(ProjectMutation.VersionConflict);
        }
    }

    private static ProjectSnapshot Snapshot(Project value) => new(
        value.Id,
        value.Key,
        value.Name,
        value.Version,
        value.CreatedAt,
        value.UpdatedAt,
        value.DeletedAt);

    private static bool IsKeyConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "project_key",
        };
}
