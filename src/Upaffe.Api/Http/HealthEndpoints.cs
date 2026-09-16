namespace Upaffe.Api.Http;

/// <summary>The foundation's process-only liveness endpoint.</summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new LivenessResponse("live")))
            .AllowAnonymous()
            .WithName("ReadLiveness")
            .WithSummary("Whether the upaffe process can answer.")
            .Produces<LivenessResponse>();

        return endpoints;
    }
}

/// <summary>A deliberately small answer that exposes no configuration.</summary>
public sealed record LivenessResponse(string Status);
