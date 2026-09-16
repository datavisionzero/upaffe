using Upaffe.Api.Hosting;

namespace Upaffe.Api.Http;

/// <summary>The instance metadata shared by every client.</summary>
public sealed record VersionResponse(string Version);

public static class InstanceEndpoints
{
    public static IEndpointRouteBuilder MapInstance(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/version", () => new VersionResponse(InstanceVersion.Value))
            .AllowAnonymous()
            .WithName("ReadVersion")
            .WithSummary("The version of this instance.")
            .Produces<VersionResponse>();
        return endpoints;
    }
}
