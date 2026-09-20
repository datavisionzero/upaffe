using Upaffe.Application.Access;

namespace Upaffe.Api.Http;

public sealed record BootstrapStateResponse(bool Required);

/// <summary>Read-only first-start state; setup is a local container command.</summary>
public static class BootstrapEndpoints
{
    public static IEndpointRouteBuilder MapBootstrap(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/bootstrap", async (
                ReadBootstrapState read,
                CancellationToken cancellationToken) =>
            {
                var state = await read.ExecuteAsync(cancellationToken);
                return Results.Ok(new BootstrapStateResponse(state.Required));
            })
            .PublicAccess()
            .WithName("ReadBootstrapState")
            .WithSummary("Whether this instance needs local operator setup.")
            .Produces<BootstrapStateResponse>();

        return endpoints;
    }
}
