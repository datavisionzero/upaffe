using System.Globalization;
using System.Text.Json.Serialization;
using Upaffe.Application.Notifications;
using Upaffe.Application.Ports;

namespace Upaffe.Api.Http;

public sealed record StartMaintenanceRequest(
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long Version,
    int DurationSeconds);

public static class MaintenanceEndpoints
{
    public static IEndpointRouteBuilder MapMaintenance(this IEndpointRouteBuilder endpoints)
    {
        var project = endpoints.MapGroup("/projects/{projectKey}/maintenance").ManagementAccess();
        project.MapGet(string.Empty, async (string projectKey, HttpContext http,
                MaintenanceActs acts, CancellationToken ct) =>
            await acts.ReadAsync(http.ActingIdentity(), "project", projectKey, null, ct))
            .WithName("ReadProjectMaintenance").WithSummary("Read direct project maintenance and effective expiry.")
            .Produces<MaintenanceSnapshot>();
        project.MapPost(string.Empty, async (string projectKey, StartMaintenanceRequest request,
                HttpContext http, MaintenanceActs acts, CancellationToken ct) =>
            await acts.StartAsync(http.ActingIdentity(), "project", projectKey, null,
                request.Version, request.DurationSeconds, ct))
            .WithName("StartProjectMaintenance").WithSummary("Start or extend finite project maintenance at its read version.")
            .Produces<MaintenanceSnapshot>();
        project.MapDelete(string.Empty, async (string projectKey, HttpContext http,
                MaintenanceActs acts, CancellationToken ct, string version = "") =>
            await acts.EndAsync(http.ActingIdentity(), "project", projectKey, null,
                Version(version), ct))
            .WithName("EndProjectMaintenance").WithSummary("End only the project maintenance window early.")
            .Produces<MaintenanceSnapshot>();

        var httpMonitor = endpoints.MapGroup("/projects/{projectKey}/http-monitors/{monitorKey}/maintenance")
            .ManagementAccess();
        httpMonitor.MapGet(string.Empty, async (string projectKey, string monitorKey,
                HttpContext http, MaintenanceActs acts, CancellationToken ct) =>
            await acts.ReadAsync(http.ActingIdentity(), "http", projectKey, monitorKey, ct))
            .WithName("ReadHttpMonitorMaintenance").WithSummary("Read direct and inherited HTTP monitor maintenance.")
            .Produces<MaintenanceSnapshot>();
        httpMonitor.MapPost(string.Empty, async (string projectKey, string monitorKey,
                StartMaintenanceRequest request, HttpContext http, MaintenanceActs acts,
                CancellationToken ct) =>
            await acts.StartAsync(http.ActingIdentity(), "http", projectKey, monitorKey,
                request.Version, request.DurationSeconds, ct))
            .WithName("StartHttpMonitorMaintenance").WithSummary("Start or extend HTTP monitor maintenance.")
            .Produces<MaintenanceSnapshot>();
        httpMonitor.MapDelete(string.Empty, async (string projectKey, string monitorKey,
                HttpContext http, MaintenanceActs acts, CancellationToken ct,
                string version = "") =>
            await acts.EndAsync(http.ActingIdentity(), "http", projectKey, monitorKey,
                Version(version), ct))
            .WithName("EndHttpMonitorMaintenance").WithSummary("End only the HTTP monitor window early.")
            .Produces<MaintenanceSnapshot>();

        var pushMonitor = endpoints.MapGroup("/projects/{projectKey}/push-monitors/{monitorKey}/maintenance")
            .ManagementAccess();
        pushMonitor.MapGet(string.Empty, async (string projectKey, string monitorKey,
                HttpContext http, MaintenanceActs acts, CancellationToken ct) =>
            await acts.ReadAsync(http.ActingIdentity(), "push", projectKey, monitorKey, ct))
            .WithName("ReadPushMonitorMaintenance").WithSummary("Read direct and inherited push monitor maintenance.")
            .Produces<MaintenanceSnapshot>();
        pushMonitor.MapPost(string.Empty, async (string projectKey, string monitorKey,
                StartMaintenanceRequest request, HttpContext http, MaintenanceActs acts,
                CancellationToken ct) =>
            await acts.StartAsync(http.ActingIdentity(), "push", projectKey, monitorKey,
                request.Version, request.DurationSeconds, ct))
            .WithName("StartPushMonitorMaintenance").WithSummary("Start or extend push monitor maintenance.")
            .Produces<MaintenanceSnapshot>();
        pushMonitor.MapDelete(string.Empty, async (string projectKey, string monitorKey,
                HttpContext http, MaintenanceActs acts, CancellationToken ct,
                string version = "") =>
            await acts.EndAsync(http.ActingIdentity(), "push", projectKey, monitorKey,
                Version(version), ct))
            .WithName("EndPushMonitorMaintenance").WithSummary("End only the push monitor window early.")
            .Produces<MaintenanceSnapshot>();
        return endpoints;
    }

    private static long Version(string value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : -1;
}
