using Upaffe.Application.Access;
using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;

namespace Upaffe.Api.Http;

public sealed record CreateCredentialRequest(string? Name);

public sealed record CredentialResponse(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RotatedAt,
    DateTimeOffset? RevokedAt);

public sealed record IssuedCredentialResponse(
    Guid Id,
    string Name,
    string Token,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RotatedAt,
    DateTimeOffset? PreviousValidUntil);

/// <summary>Explicit one-time issuance and metadata-only credential administration.</summary>
public static class ManagementCredentialEndpoints
{
    public static IEndpointRouteBuilder MapManagementCredentials(this IEndpointRouteBuilder endpoints)
    {
        var credentials = endpoints.MapGroup("/management-credentials")
            .RequireAuthorization(BrowserAuthentication.ManagementPolicy);

        credentials.MapPost(string.Empty, async (
                CreateCredentialRequest request,
                HttpContext http,
                CreateManagementCredential create,
                CancellationToken cancellationToken) =>
            {
                var issued = await create.ExecuteAsync(
                    Current(http),
                    request.Name,
                    cancellationToken);
                return Results.Created(
                    $"/api/management-credentials/{issued.Credential.Id}",
                    Response(issued));
            })
            .WithName("CreateManagementCredential")
            .WithSummary("Create a named management credential and reveal its token once.")
            .Produces<IssuedCredentialResponse>(StatusCodes.Status201Created)
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

        credentials.MapGet(string.Empty, async (
                HttpContext http,
                ListManagementCredentials list,
                CancellationToken cancellationToken) =>
            {
                var rows = await list.ExecuteAsync(Current(http), cancellationToken);
                return Results.Ok(rows.Select(Response));
            })
            .WithName("ListManagementCredentials")
            .WithSummary("List management credential metadata without any token.")
            .Produces<IReadOnlyList<CredentialResponse>>()
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json");

        credentials.MapPost("/{id:guid}/rotate", async (
                Guid id,
                HttpContext http,
                RotateManagementCredential rotate,
                CancellationToken cancellationToken) =>
            Results.Ok(Response(await rotate.ExecuteAsync(
                Current(http),
                id,
                cancellationToken))))
            .WithName("RotateManagementCredential")
            .WithSummary("Rotate a credential, reveal the new token once, and overlap the prior token briefly.")
            .Produces<IssuedCredentialResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

        credentials.MapDelete("/{id:guid}", async (
                Guid id,
                HttpContext http,
                RevokeManagementCredential revoke,
                CancellationToken cancellationToken) =>
            {
                await revoke.ExecuteAsync(Current(http), id, cancellationToken);
                return Results.NoContent();
            })
            .WithName("RevokeManagementCredential")
            .WithSummary("Revoke every token belonging to a management credential immediately.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json");

        return endpoints;
    }

    private static Identity Current(HttpContext context) =>
        context.Features.Get<Identity>() ?? throw Refusal.AuthenticationRequired();

    private static CredentialResponse Response(CredentialMetadata value) => new(
        value.Id,
        value.Name,
        value.CreatedAt,
        value.RotatedAt,
        value.RevokedAt);

    private static IssuedCredentialResponse Response(IssuedCredential value) => new(
        value.Credential.Id,
        value.Credential.Name,
        value.Token,
        value.Credential.CreatedAt,
        value.Credential.RotatedAt,
        value.PreviousValidUntil);
}
