namespace Upaffe.Application.Ports;

public sealed record ProjectSnapshot(
    Guid Id,
    string Key,
    string Name,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt,
    IReadOnlyList<string> Recipients);

public enum ProjectMutation
{
    Changed,
    Unchanged,
    Missing,
    Conflict,
    VersionConflict,
    Deleted,
}

public sealed record ProjectMutationResult(
    ProjectMutation Outcome,
    ProjectSnapshot? Project = null);

public interface IProjectStore
{
    Task<ProjectMutationResult> CreateAsync(
        string key,
        string name,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<ProjectSnapshot?> GetAsync(string key, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProjectSnapshot>> ListAsync(
        bool deleted,
        CancellationToken cancellationToken);

    Task<ProjectMutationResult> RenameAsync(
        string key,
        string name,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<ProjectMutationResult> DeleteAsync(
        string key,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<ProjectMutationResult> RestoreAsync(
        string key,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<ProjectMutationResult> ReplaceRecipientsAsync(
        string key,
        string[] recipients,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
