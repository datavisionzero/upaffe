using System.Globalization;
using System.Text.Json.Serialization;
using Upaffe.Application.Monitoring;
using Upaffe.Application.Ports;

namespace Upaffe.Api.Http;

public sealed record HttpHeaderInput(string? Name, string? Value);

public sealed record CreateHttpMonitorRequest(
    string? Key,
    string? Name,
    string? TargetUrl,
    int ExpectedStatusCode,
    string? TextCondition,
    string? TextFragment,
    int IntervalSeconds,
    int TimeoutSeconds,
    int FailureThreshold,
    string? Instruction,
    string? RunbookUrl,
    IReadOnlyList<HttpHeaderInput>? Headers);

public sealed record UpdateHttpMonitorRequest(
    string? Name,
    string? TargetUrl,
    int ExpectedStatusCode,
    string? TextCondition,
    string? TextFragment,
    int IntervalSeconds,
    int TimeoutSeconds,
    int FailureThreshold,
    string? Instruction,
    string? RunbookUrl,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long Version);

public sealed record HttpMonitorVersionRequest(
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long Version);

public sealed record SetHttpMonitorHeaderRequest(
    string? Value,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long Version);

public sealed record HttpHeaderResponse(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record HttpMonitorResponse(
    Guid Id,
    string ProjectKey,
    string Key,
    string Name,
    string TargetUrl,
    bool HasTargetQuery,
    int ExpectedStatusCode,
    string TextCondition,
    string? TextFragment,
    int IntervalSeconds,
    int TimeoutSeconds,
    int FailureThreshold,
    string? Instruction,
    string? RunbookUrl,
    string State,
    int ConsecutiveFailures,
    DateTimeOffset? NextCheckAt,
    Guid? LatestResultId,
    Guid? LatestSuccessId,
    Guid? OpenIncidentId,
    IReadOnlyList<HttpHeaderResponse> Headers,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PausedAt,
    DateTimeOffset? DeletedAt);

public sealed record HttpMonitorTestResponse(
    Guid CheckId,
    bool AppliedToCurrentState,
    bool Succeeded,
    string? ReasonCode,
    string Message,
    int? StatusCode,
    int ResponseTimeMilliseconds,
    string? EffectiveUrl,
    HttpMonitorResponse Monitor);

public sealed record HttpCheckHistoryResponse(
    Guid Id,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long Sequence,
    string Trigger,
    DateTimeOffset ScheduledFor,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string Outcome,
    string? FailureReason,
    int? StatusCode,
    int? ResponseTimeMilliseconds,
    string? EffectiveUrl,
    bool? AppliedToCurrentState);

public sealed record HttpCheckHistoryPageResponse(
    IReadOnlyList<HttpCheckHistoryResponse> Items,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long? NextBeforeSequence);

public sealed record IncidentHistoryResponse(
    Guid Id,
    Guid FirstFailureCheckId,
    Guid OpeningCheckId,
    Guid LatestFailureCheckId,
    Guid? ResolutionCheckId,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long FirstFailureSequence,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long OpeningSequence,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long LatestFailureSequence,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long? ResolutionSequence,
    DateTimeOffset BeganAt,
    DateTimeOffset OpenedAt,
    DateTimeOffset LastObservedAt,
    DateTimeOffset? ResolvedAt,
    string OriginalReason,
    string LatestReason);

public sealed record IncidentHistoryPageResponse(
    IReadOnlyList<IncidentHistoryResponse> Items,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long? NextBeforeOpeningSequence);

public static class HttpMonitorEndpoints
{
    public static IEndpointRouteBuilder MapHttpMonitors(this IEndpointRouteBuilder endpoints)
    {
        var monitors = endpoints.MapGroup("/projects/{projectKey}/http-monitors").ManagementAccess();

        monitors.MapPost(string.Empty, async (
                string projectKey,
                CreateHttpMonitorRequest request,
                HttpContext http,
                CreateHttpMonitor create,
                CancellationToken cancellationToken) =>
            {
                var result = await create.ExecuteAsync(
                    http.ActingIdentity(),
                    projectKey,
                    request.Key,
                    request.Name,
                    request.TargetUrl,
                    request.ExpectedStatusCode,
                    request.TextCondition,
                    request.TextFragment,
                    request.IntervalSeconds,
                    request.TimeoutSeconds,
                    request.FailureThreshold,
                    request.Instruction,
                    request.RunbookUrl,
                    request.Headers?.Select(value => new HttpHeaderValue(
                        value.Name ?? string.Empty,
                        value.Value ?? string.Empty)).ToArray(),
                    cancellationToken);
                var response = Response(result.Monitor);
                return result.Created
                    ? Results.Created(
                        $"/api/projects/{projectKey}/http-monitors/{result.Monitor.Key}",
                        response)
                    : Results.Ok(response);
            })
            .WithName("CreateHttpMonitor")
            .WithSummary("Create an HTTP monitor idempotently by its project-scoped key.")
            .Produces<HttpMonitorResponse>(StatusCodes.Status201Created)
            .Produces<HttpMonitorResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

        monitors.MapGet(string.Empty, async (
                string projectKey,
                HttpContext http,
                ListHttpMonitors list,
                CancellationToken cancellationToken) =>
            (await list.ExecuteAsync(http.ActingIdentity(), projectKey, cancellationToken))
                .Select(Response))
            .WithName("ListHttpMonitors")
            .WithSummary("List live HTTP monitors in one project.")
            .Produces<IReadOnlyList<HttpMonitorResponse>>()
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json");

        monitors.MapGet("/{monitorKey}", async (
                string projectKey,
                string monitorKey,
                HttpContext http,
                ReadHttpMonitor read,
                CancellationToken cancellationToken) =>
            Response(await read.ExecuteAsync(
                http.ActingIdentity(),
                projectKey,
                monitorKey,
                cancellationToken)))
            .WithName("ReadHttpMonitor")
            .WithSummary("Read an HTTP monitor without returning secret values.")
            .Produces<HttpMonitorResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json");

        monitors.MapGet("/{monitorKey}/checks", async (
                string projectKey,
                string monitorKey,
                long? before_sequence,
                int? limit,
                HttpContext http,
                ListHttpCheckHistory list,
                CancellationToken cancellationToken) =>
            CheckHistoryResponse(await list.ExecuteAsync(
                http.ActingIdentity(),
                projectKey,
                monitorKey,
                before_sequence,
                limit,
                cancellationToken)))
            .WithName("ListHttpCheckHistory")
            .WithSummary("List completed HTTP checks newest first with cursor pagination.")
            .Produces<HttpCheckHistoryPageResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json");

        monitors.MapGet("/{monitorKey}/checks/{checkId:guid}", async (
                string projectKey,
                string monitorKey,
                Guid checkId,
                HttpContext http,
                ReadHttpCheckEvidence read,
                CancellationToken cancellationToken) =>
            CheckHistoryResponse(await read.ExecuteAsync(
                http.ActingIdentity(), projectKey, monitorKey, checkId, cancellationToken)))
            .WithName("ReadHttpCheckEvidence")
            .WithSummary("Read one completed HTTP check in this live monitor, including retained current evidence.")
            .Produces<HttpCheckHistoryResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json");

        monitors.MapGet("/{monitorKey}/incidents", async (
                string projectKey,
                string monitorKey,
                long? before_opening_sequence,
                int? limit,
                HttpContext http,
                ListIncidentHistory list,
                CancellationToken cancellationToken) =>
            IncidentHistoryResponse(await list.ExecuteAsync(
                http.ActingIdentity(),
                projectKey,
                monitorKey,
                before_opening_sequence,
                limit,
                cancellationToken)))
            .WithName("ListIncidentHistory")
            .WithSummary("List HTTP incidents newest first with cursor pagination.")
            .Produces<IncidentHistoryPageResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json");

        monitors.MapGet("/{monitorKey}/incidents/{incidentId:guid}", async (
                string projectKey,
                string monitorKey,
                Guid incidentId,
                HttpContext http,
                ReadHttpIncidentEvidence read,
                CancellationToken cancellationToken) =>
            IncidentHistoryResponse(await read.ExecuteAsync(
                http.ActingIdentity(), projectKey, monitorKey, incidentId, cancellationToken)))
            .WithName("ReadHttpIncidentEvidence")
            .WithSummary("Read one retained HTTP incident in this live monitor.")
            .Produces<IncidentHistoryResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json");

        monitors.MapPut("/{monitorKey}", async (
                string projectKey,
                string monitorKey,
                UpdateHttpMonitorRequest request,
                HttpContext http,
                UpdateHttpMonitor update,
                CancellationToken cancellationToken) =>
            Response(await update.ExecuteAsync(
                http.ActingIdentity(),
                projectKey,
                monitorKey,
                request.Name,
                request.TargetUrl,
                request.ExpectedStatusCode,
                request.TextCondition,
                request.TextFragment,
                request.IntervalSeconds,
                request.TimeoutSeconds,
                request.FailureThreshold,
                request.Instruction,
                request.RunbookUrl,
                request.Version,
                cancellationToken)))
            .WithName("UpdateHttpMonitor")
            .WithSummary("Update non-secret HTTP monitor configuration and optionally replace its target URL.")
            .Produces<HttpMonitorResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

        monitors.MapDelete("/{monitorKey}", async (
                string projectKey,
                string monitorKey,
                HttpContext http,
                RemoveHttpMonitor remove,
                CancellationToken cancellationToken,
                string version = "") =>
            Response(await remove.ExecuteAsync(
                http.ActingIdentity(),
                projectKey,
                monitorKey,
                ParsedVersion(version),
                cancellationToken)))
            .WithName("RemoveHttpMonitor")
            .WithSummary("Remove an HTTP monitor while retaining its history and key.")
            .Produces<HttpMonitorResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

        monitors.MapPost("/{monitorKey}/pause", async (
                string projectKey,
                string monitorKey,
                HttpMonitorVersionRequest request,
                HttpContext http,
                PauseHttpMonitor pause,
                CancellationToken cancellationToken) =>
            Response(await pause.ExecuteAsync(
                http.ActingIdentity(), projectKey, monitorKey, request.Version, cancellationToken)))
            .WithName("PauseHttpMonitor")
            .WithSummary("Pause an HTTP monitor at the version last read.")
            .Produces<HttpMonitorResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

        monitors.MapPost("/{monitorKey}/resume", async (
                string projectKey,
                string monitorKey,
                HttpMonitorVersionRequest request,
                HttpContext http,
                ResumeHttpMonitor resume,
                CancellationToken cancellationToken) =>
            Response(await resume.ExecuteAsync(
                http.ActingIdentity(), projectKey, monitorKey, request.Version, cancellationToken)))
            .WithName("ResumeHttpMonitor")
            .WithSummary("Resume an HTTP monitor and make a fresh check due.")
            .Produces<HttpMonitorResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

        monitors.MapPut("/{monitorKey}/headers/{name}", async (
                string projectKey,
                string monitorKey,
                string name,
                SetHttpMonitorHeaderRequest request,
                HttpContext http,
                SetHttpMonitorHeader set,
                CancellationToken cancellationToken) =>
            Response(await set.ExecuteAsync(
                http.ActingIdentity(),
                projectKey,
                monitorKey,
                name,
                request.Value,
                request.Version,
                cancellationToken)))
            .WithName("SetHttpMonitorHeader")
            .WithSummary("Set or replace one write-only HTTP request header.")
            .Produces<HttpMonitorResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

        monitors.MapDelete("/{monitorKey}/headers/{name}", async (
                string projectKey,
                string monitorKey,
                string name,
                HttpContext http,
                RemoveHttpMonitorHeader remove,
                CancellationToken cancellationToken,
                string version = "") =>
            Response(await remove.ExecuteAsync(
                http.ActingIdentity(),
                projectKey,
                monitorKey,
                name,
                ParsedVersion(version),
                cancellationToken)))
            .WithName("RemoveHttpMonitorHeader")
            .WithSummary("Remove one write-only HTTP request header.")
            .Produces<HttpMonitorResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

        monitors.MapPost("/{monitorKey}/test", async (
                string projectKey,
                string monitorKey,
                HttpContext http,
                TestHttpMonitor test,
                CancellationToken cancellationToken) =>
            TestResponse(await test.ExecuteAsync(
                http.ActingIdentity(), projectKey, monitorKey, cancellationToken)))
            .WithName("TestHttpMonitor")
            .WithSummary("Run one immediate HTTP check through the shared bounded executor.")
            .Produces<HttpMonitorTestResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status404NotFound, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

        return endpoints;
    }

    private static HttpMonitorResponse Response(HttpMonitorSnapshot value) => new(
        value.Id,
        value.ProjectKey,
        value.Key,
        value.Name,
        value.TargetUrl,
        value.HasTargetQuery,
        value.ExpectedStatusCode,
        value.TextCondition.ToString().ToLowerInvariant(),
        value.TextFragment,
        value.IntervalSeconds,
        value.TimeoutSeconds,
        value.FailureThreshold,
        value.Instruction,
        value.RunbookUrl,
        value.State.ToString().ToLowerInvariant(),
        value.ConsecutiveFailures,
        value.NextCheckAt,
        value.LatestResultId,
        value.LatestSuccessId,
        value.OpenIncidentId,
        value.Headers.Select(header => new HttpHeaderResponse(
            header.Id,
            header.Name,
            header.CreatedAt,
            header.UpdatedAt)).ToArray(),
        value.Version,
        value.CreatedAt,
        value.UpdatedAt,
        value.PausedAt,
        value.DeletedAt);

    private static HttpMonitorTestResponse TestResponse(CompletedHttpTest value) => new(
        value.CheckId,
        value.AppliedToCurrentState,
        value.Result.Succeeded,
        value.Result.ReasonCode,
        value.Result.Message,
        value.Result.StatusCode,
        value.Result.ResponseTimeMilliseconds,
        value.Result.EffectiveUrl,
        Response(value.Monitor));

    private static HttpCheckHistoryResponse CheckHistoryResponse(HttpCheckHistoryItem item) => new(
            item.Id,
            item.Sequence,
            item.Trigger.ToString().ToLowerInvariant(),
            item.ScheduledFor,
            item.StartedAt,
            item.CompletedAt,
            item.Outcome.ToString().ToLowerInvariant(),
            item.FailureReason,
            item.StatusCode,
            item.ResponseTimeMilliseconds,
            item.EffectiveUrl,
            item.AppliedToCurrentState);

    private static HttpCheckHistoryPageResponse CheckHistoryResponse(HttpCheckHistoryPage value) => new(
        value.Items.Select(CheckHistoryResponse).ToArray(),
        value.NextBeforeSequence);

    private static IncidentHistoryResponse IncidentHistoryResponse(IncidentHistoryItem item) => new(
            item.Id,
            item.FirstFailureCheckId,
            item.OpeningCheckId,
            item.LatestFailureCheckId,
            item.ResolutionCheckId,
            item.FirstFailureSequence,
            item.OpeningSequence,
            item.LatestFailureSequence,
            item.ResolutionSequence,
            item.BeganAt,
            item.OpenedAt,
            item.LastObservedAt,
            item.ResolvedAt,
            item.OriginalReason,
            item.LatestReason);

    private static IncidentHistoryPageResponse IncidentHistoryResponse(IncidentHistoryPage value) => new(
        value.Items.Select(IncidentHistoryResponse).ToArray(),
        value.NextBeforeOpeningSequence);

    private static long? ParsedVersion(string value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
