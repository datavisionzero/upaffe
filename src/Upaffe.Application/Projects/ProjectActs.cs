using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;
using Upaffe.Domain.Projects;

namespace Upaffe.Application.Projects;

public sealed record CreatedProject(ProjectSnapshot Project, bool Created);

public sealed class CreateProject(IProjectStore projects, TimeProvider clock)
{
    public async Task<CreatedProject> ExecuteAsync(
        Identity identity,
        string? key,
        string? name,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var acceptedKey = ProjectValidation.ValidKey(key);
        var acceptedName = ProjectValidation.ValidName(name);
        var result = await projects.CreateAsync(
            acceptedKey,
            acceptedName,
            clock.GetUtcNow(),
            cancellationToken);
        return result.Outcome switch
        {
            ProjectMutation.Changed when result.Project is not null => new(result.Project, true),
            ProjectMutation.Unchanged when result.Project is not null => new(result.Project, false),
            ProjectMutation.Conflict => throw Refusal.Conflict(
                "A project already uses that key with different facts."),
            _ => throw ProjectValidation.InvalidStoreResult("create"),
        };
    }
}

public sealed class ReadProject(IProjectStore projects)
{
    public async Task<ProjectSnapshot> ExecuteAsync(
        Identity identity,
        string? key,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var project = await projects.GetAsync(ProjectValidation.ValidKey(key), cancellationToken);
        return project ?? throw Refusal.NotFound("No such project.");
    }
}

public sealed class ListProjects(IProjectStore projects)
{
    public Task<IReadOnlyList<ProjectSnapshot>> ExecuteAsync(
        Identity identity,
        bool deleted,
        CancellationToken cancellationToken)
    {
        _ = identity;
        return projects.ListAsync(deleted, cancellationToken);
    }
}

public sealed class RenameProject(IProjectStore projects, TimeProvider clock)
{
    public async Task<ProjectSnapshot> ExecuteAsync(
        Identity identity,
        string? key,
        string? name,
        long? version,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var result = await projects.RenameAsync(
            ProjectValidation.ValidKey(key),
            ProjectValidation.ValidName(name),
            ProjectValidation.ValidVersion(version),
            clock.GetUtcNow(),
            cancellationToken);
        return ProjectValidation.Changed(result, "rename");
    }
}

public sealed class DeleteProject(IProjectStore projects, TimeProvider clock)
{
    public async Task<ProjectSnapshot> ExecuteAsync(
        Identity identity,
        string? key,
        long? version,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var result = await projects.DeleteAsync(
            ProjectValidation.ValidKey(key),
            ProjectValidation.ValidVersion(version),
            clock.GetUtcNow(),
            cancellationToken);
        return ProjectValidation.Changed(result, "delete", allowUnchanged: true);
    }
}

public sealed class RestoreProject(IProjectStore projects, TimeProvider clock)
{
    public async Task<ProjectSnapshot> ExecuteAsync(
        Identity identity,
        string? key,
        long? version,
        CancellationToken cancellationToken)
    {
        _ = identity;
        var result = await projects.RestoreAsync(
            ProjectValidation.ValidKey(key),
            ProjectValidation.ValidVersion(version),
            clock.GetUtcNow(),
            cancellationToken);
        return ProjectValidation.Changed(result, "restore", allowUnchanged: true);
    }
}

internal static class ProjectValidation
{
    public static string ValidKey(string? value) => Validate(
        "key",
        () => Project.ValidateKey(value ?? string.Empty));

    public static string ValidName(string? value) => Validate(
        "name",
        () => Project.ValidateName(value ?? string.Empty));

    public static long ValidVersion(long? value)
    {
        if (value is null or <= 0)
        {
            throw Refusal.Validation(new Dictionary<string, string[]>
            {
                ["version"] = ["A positive project version is required."],
            });
        }

        return value.Value;
    }

    public static ProjectSnapshot Changed(
        ProjectMutationResult result,
        string operation,
        bool allowUnchanged = false) => result.Outcome switch
    {
        ProjectMutation.Changed when result.Project is not null => result.Project,
        ProjectMutation.Unchanged when allowUnchanged && result.Project is not null => result.Project,
        ProjectMutation.Missing => throw Refusal.NotFound("No such project."),
        ProjectMutation.Deleted => throw Refusal.Conflict(
            "A deleted project must be restored before it changes."),
        ProjectMutation.VersionConflict => throw Refusal.Conflict(
            "The project changed after the supplied version was read."),
        _ => throw InvalidStoreResult(operation),
    };

    private static T Validate<T>(string field, Func<T> accept)
    {
        try
        {
            return accept();
        }
        catch (ArgumentException exception)
        {
            throw Refusal.Validation(new Dictionary<string, string[]> { [field] = [exception.Message] });
        }
    }

    public static InvalidOperationException InvalidStoreResult(string operation) =>
        new($"The project store returned an invalid {operation} outcome.");
}
