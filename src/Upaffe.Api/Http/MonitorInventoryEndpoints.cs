using Upaffe.Application.Monitoring;
using Upaffe.Application.Ports;

namespace Upaffe.Api.Http;

public static class MonitorInventoryEndpoints
{
    public static IEndpointRouteBuilder MapMonitorInventory(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/monitors", async (string? project, string? state, string? type,
                string? q, int? limit, int? offset, HttpContext http,
                ReadMonitorInventory read, CancellationToken cancellationToken) =>
            await read.ExecuteAsync(http.ActingIdentity(), project, state, type, q,
                limit, offset, cancellationToken))
            .ManagementAccess()
            .WithName("ReadMonitorInventory")
            .WithSummary("Search current HTTP and push monitors across live projects.")
            .Produces<MonitorInventoryPage>()
            .Produces<ProblemResponse>(400)
            .Produces<ProblemResponse>(401);
        return endpoints;
    }
}
