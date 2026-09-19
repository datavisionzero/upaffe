using Upaffe.Api.Hosting;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.Api.Http;

/// <summary>Separate process, database, and monitoring progress signals.</summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new LivenessResponse("live")))
            .PublicAccess()
            .WithName("ReadLiveness")
            .WithSummary("Whether the upaffe process can answer.")
            .Produces<LivenessResponse>();

        endpoints.MapGet("/health/ready", async (
                SchemaMigrator migrator,
                ILoggerFactory loggers,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return await migrator.AppliedAsync(cancellationToken)
                        ? Results.Ok(new LivenessResponse("ready"))
                        : Results.Json(
                            new LivenessResponse("not-ready"),
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    loggers.CreateLogger(typeof(HealthEndpoints)).LogWarning(
                        exception, "Readiness could not be established.");
                    return Results.Json(
                        new LivenessResponse("not-ready"),
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            })
            .PublicAccess()
            .WithName("ReadReadiness")
            .WithSummary("Whether PostgreSQL answers with the expected schema.")
            .Produces<LivenessResponse>()
            .Produces<LivenessResponse>(StatusCodes.Status503ServiceUnavailable);

        endpoints.MapGet("/health/progress", (HttpContext context, MonitoringProgress progress) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return progress.BothFresh()
                    ? Results.Ok(new LivenessResponse("progressing"))
                    : Results.Json(
                        new LivenessResponse("stalled"),
                        statusCode: StatusCodes.Status503ServiceUnavailable);
            })
            .PublicAccess()
            .WithName("ReadMonitoringProgress")
            .WithSummary("Whether both monitoring workers recently completed database-backed work or an idle poll.")
            .Produces<LivenessResponse>()
            .Produces<LivenessResponse>(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }
}

/// <summary>A deliberately small answer that exposes no configuration.</summary>
public sealed record LivenessResponse(string Status);
