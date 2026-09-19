using System.Globalization;
using System.Text.Json.Serialization;
using Upaffe.Application.Ports;
using Upaffe.Application.Projects;

namespace Upaffe.Api.Http;

public sealed record CreateProjectRequest(string? Key, string? Name);
public sealed record RenameProjectRequest(
    string? Name,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long Version);
public sealed record ProjectVersionRequest(
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long Version);

public sealed record ProjectResponse(
    Guid Id,
    string Key,
    string Name,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt);

/// <summary>The complete management surface for stable, recoverable projects.</summary>
public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjects(this IEndpointRouteBuilder endpoints)
    {
        var projects = endpoints.MapGroup("/projects").ManagementAccess();

        endpoints.MapGet("/overview", async (
                HttpContext http,
                ReadInstanceOverview read,
                CancellationToken cancellationToken) =>
            await read.ExecuteAsync(http.ActingIdentity(), cancellationToken))
            .ManagementAccess()
            .WithName("ReadInstanceOverview")
            .WithSummary("Read current, safe health across all live projects.")
            .Produces<InstanceOverview>()
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json");

        projects.MapPost(string.Empty, async (
                CreateProjectRequest request,
                HttpContext http,
                CreateProject create,
                CancellationToken cancellationToken) =>
            {
                var result = await create.ExecuteAsync(
                    http.ActingIdentity(),
                    request.Key,
                    request.Name,
                    cancellationToken);
                var response = Response(result.Project);
                return result.Created
                    ? Results.Created($"/api/projects/{result.Project.Key}", response)
                    : Results.Ok(response);
            })
            .WithName("CreateProject")
            .WithSummary("Create a project idempotently by its immutable key.")
            .Produces<ProjectResponse>(StatusCodes.Status201Created)
            .Produces<ProjectResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

        projects.MapGet(string.Empty, async (
                HttpContext http,
                ListProjects list,
                CancellationToken cancellationToken,
                bool deleted = false) =>
            (await list.ExecuteAsync(http.ActingIdentity(), deleted, cancellationToken))
                .Select(Response))
            .WithName("ListProjects")
            .WithSummary("List live projects, or deleted projects when deleted=true.")
            .Produces<IReadOnlyList<ProjectResponse>>()
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json");

        projects.MapGet("/{key}", async (
                string key,
                HttpContext http,
                ReadProject read,
                CancellationToken cancellationToken) =>
            Response(await read.ExecuteAsync(
                http.ActingIdentity(),
                key,
                cancellationToken)))
            .WithName("ReadProject")
            .WithSummary("Read a live or deleted project by its immutable key.")
            .Produces<ProjectResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json");

        projects.MapGet("/{key}/report", async (
                string key,
                HttpContext http,
                ReadProjectReport read,
                CancellationToken cancellationToken) =>
            await read.ExecuteAsync(http.ActingIdentity(), key, cancellationToken))
            .WithName("ReadProjectReport")
            .WithSummary("Read one safe, current investigation report for a live project.")
            .Produces<ProjectReport>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json");

        projects.MapPut("/{key}", async (
                string key,
                RenameProjectRequest request,
                HttpContext http,
                RenameProject rename,
                CancellationToken cancellationToken) =>
            Response(await rename.ExecuteAsync(
                http.ActingIdentity(),
                key,
                request.Name,
                request.Version,
                cancellationToken)))
            .WithName("RenameProject")
            .WithSummary("Rename a live project at the version last read.")
            .Produces<ProjectResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

        projects.MapDelete("/{key}", async (
                string key,
                HttpContext http,
                DeleteProject delete,
                CancellationToken cancellationToken,
                string version = "") =>
            Response(await delete.ExecuteAsync(
                http.ActingIdentity(),
                key,
                ParsedVersion(version),
                cancellationToken)))
            .WithName("DeleteProject")
            .WithSummary("Soft-delete a project at the version last read.")
            .Produces<ProjectResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

        projects.MapPost("/{key}/restore", async (
                string key,
                ProjectVersionRequest request,
                HttpContext http,
                RestoreProject restore,
                CancellationToken cancellationToken) =>
            Response(await restore.ExecuteAsync(
                http.ActingIdentity(),
                key,
                request.Version,
                cancellationToken)))
            .WithName("RestoreProject")
            .WithSummary("Restore a deleted project at the version last read.")
            .Produces<ProjectResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

        return endpoints;
    }

    private static ProjectResponse Response(ProjectSnapshot value) => new(
        value.Id,
        value.Key,
        value.Name,
        value.Version,
        value.CreatedAt,
        value.UpdatedAt,
        value.DeletedAt);

    private static long? ParsedVersion(string value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
