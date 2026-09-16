using Upaffe.Application.Access;

namespace Upaffe.Api.Http;

public sealed record BootstrapStateResponse(bool Required, bool Available);

public sealed record BootstrapRequest(string? Proof, string? Email, string? Password);

/// <summary>The public but short-lived path that establishes the sole operator.</summary>
public static class BootstrapEndpoints
{
    public static IEndpointRouteBuilder MapBootstrap(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/bootstrap", async (
                ReadBootstrapState read,
                CancellationToken cancellationToken) =>
            {
                var state = await read.ExecuteAsync(cancellationToken);
                return Results.Ok(new BootstrapStateResponse(state.Required, state.Available));
            })
            .AllowAnonymous()
            .WithName("ReadBootstrapState")
            .WithSummary("Whether this instance still needs and can accept its one-time bootstrap.")
            .Produces<BootstrapStateResponse>();

        endpoints.MapPost("/bootstrap", async (
                BootstrapRequest request,
                EstablishOperator establish,
                CancellationToken cancellationToken) =>
            {
                await establish.ExecuteAsync(
                    request.Proof,
                    request.Email,
                    request.Password,
                    cancellationToken);
                return Results.NoContent();
            })
            .AllowAnonymous()
            .WithName("EstablishOperator")
            .WithSummary("Establish the sole operator using the bounded bootstrap proof.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

        return endpoints;
    }
}
