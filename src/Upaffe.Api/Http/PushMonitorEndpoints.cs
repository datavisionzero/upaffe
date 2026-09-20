using System.Text.Json.Serialization;
using Upaffe.Application.Monitoring;
using Upaffe.Application.Ports;

namespace Upaffe.Api.Http;

public sealed record CreatePushMonitorRequest(string? Key, string? Name, string? Mode,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] int? IntervalSeconds,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] int? ToleranceSeconds,
    string? Instruction, string? RunbookUrl, string? Purpose = null);

public sealed record UpdatePushMonitorRequest(string? Name,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] int? IntervalSeconds,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] int? ToleranceSeconds,
    string? Instruction, string? RunbookUrl,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long? Version, string? Purpose = null);

public sealed record PushMonitorVersionRequest([property: JsonNumberHandling(JsonNumberHandling.Strict)] long? Version);

public sealed record PushMonitorResponse(Guid Id, string ProjectKey, string Key, string Name, string Mode,
    int IntervalSeconds, int ToleranceSeconds, string? Instruction, string? RunbookUrl, string State,
    DateTimeOffset? LastReceivedAt, DateTimeOffset? NextDeadlineAt, Guid? LatestReportId, Guid? LatestSuccessId,
    Guid? OpenIncidentId, bool HasReportingCredential, long Version, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, DateTimeOffset? PausedAt, DateTimeOffset? DeletedAt, string? Purpose = null);

public sealed record ReportingCredentialResponse(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset? RotatedAt, DateTimeOffset? RevokedAt);
public sealed record IssuedReportingCredentialResponse(Guid Id, string Token, string ReportUrl, DateTimeOffset CreatedAt,
    DateTimeOffset? RotatedAt, DateTimeOffset? PreviousValidUntil);

public sealed record PushReportHistoryResponse(
    Guid Id,
    Guid ReportId,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long EvaluationGeneration,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long Sequence,
    DateTimeOffset ObservedAt,
    DateTimeOffset ReceivedAt,
    string Outcome,
    string? Reason,
    bool Applicable);

public sealed record PushReportHistoryPageResponse(
    IReadOnlyList<PushReportHistoryResponse> Items,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long? NextBeforeSequence);

public sealed record PushIncidentHistoryResponse(
    Guid Id,
    Guid OpeningReportId,
    Guid LatestFailureReportId,
    Guid? ResolutionReportId,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long OpeningSequence,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long LatestFailureSequence,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long? ResolutionSequence,
    DateTimeOffset BeganAt,
    DateTimeOffset OpenedAt,
    DateTimeOffset LastObservedAt,
    DateTimeOffset? ResolvedAt,
    string OriginalReason,
    string LatestReason);

public sealed record PushIncidentHistoryPageResponse(
    IReadOnlyList<PushIncidentHistoryResponse> Items,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long? NextBeforeOpeningSequence);

public static class PushMonitorEndpoints
{
    public static IEndpointRouteBuilder MapPushMonitors(this IEndpointRouteBuilder endpoints)
    {
        var monitors = endpoints.MapGroup("/projects/{projectKey}/push-monitors").ManagementAccess();
        monitors.MapPost(string.Empty, async (string projectKey, CreatePushMonitorRequest request, HttpContext http,
            CreatePushMonitor create, CancellationToken cancellationToken) =>
        {
            var result = await create.ExecuteAsync(http.ActingIdentity(), projectKey, request.Key, request.Name, request.Mode,
                request.IntervalSeconds, request.ToleranceSeconds, request.Instruction, request.RunbookUrl, cancellationToken, request.Purpose);
            return result.Created
                ? Results.Created($"/api/projects/{projectKey}/push-monitors/{result.Monitor.Key}", Response(result.Monitor))
                : Results.Ok(Response(result.Monitor));
        }).WithName("CreatePushMonitor").WithSummary("Create a push monitor idempotently by its project-scoped key.")
            .Produces<PushMonitorResponse>(201).Produces<PushMonitorResponse>().Produces<ProblemResponse>(400).Produces<ProblemResponse>(401).Produces<ProblemResponse>(404).Produces<ProblemResponse>(409);

        monitors.MapGet(string.Empty, async (string projectKey, HttpContext http, ListPushMonitors list, CancellationToken cancellationToken) =>
            (await list.ExecuteAsync(http.ActingIdentity(), projectKey, cancellationToken)).Select(Response))
            .WithName("ListPushMonitors").WithSummary("List live push monitors in one project.").Produces<IReadOnlyList<PushMonitorResponse>>().Produces<ProblemResponse>(401).Produces<ProblemResponse>(404);

        monitors.MapGet("/{monitorKey}", async (string projectKey, string monitorKey, HttpContext http, ReadPushMonitor read, CancellationToken cancellationToken) =>
            Response(await read.ExecuteAsync(http.ActingIdentity(), projectKey, monitorKey, cancellationToken)))
            .WithName("ReadPushMonitor").WithSummary("Read a push monitor without its reporting secret.").Produces<PushMonitorResponse>().Produces<ProblemResponse>(401).Produces<ProblemResponse>(404);

        monitors.MapGet("/{monitorKey}/reports", async (
                string projectKey,
                string monitorKey,
                long? before_sequence,
                int? limit,
                HttpContext http,
                ListPushReportHistory list,
                CancellationToken cancellationToken) =>
            ReportHistoryResponse(await list.ExecuteAsync(
                http.ActingIdentity(), projectKey, monitorKey, before_sequence, limit, cancellationToken)))
            .WithName("ListPushReportHistory")
            .WithSummary("List push reports newest first with cursor pagination.")
            .Produces<PushReportHistoryPageResponse>()
            .Produces<ProblemResponse>(400)
            .Produces<ProblemResponse>(401)
            .Produces<ProblemResponse>(404);

        monitors.MapGet("/{monitorKey}/reports/{reportId:guid}", async (
                string projectKey,
                string monitorKey,
                Guid reportId,
                HttpContext http,
                ReadPushReportEvidence read,
                CancellationToken cancellationToken) =>
            ReportHistoryResponse(await read.ExecuteAsync(
                http.ActingIdentity(), projectKey, monitorKey, reportId, cancellationToken)))
            .WithName("ReadPushReportEvidence")
            .WithSummary("Read one push report in this live monitor, including retained current evidence.")
            .Produces<PushReportHistoryResponse>()
            .Produces<ProblemResponse>(400)
            .Produces<ProblemResponse>(401)
            .Produces<ProblemResponse>(404);

        monitors.MapGet("/{monitorKey}/incidents", async (
                string projectKey,
                string monitorKey,
                long? before_opening_sequence,
                int? limit,
                HttpContext http,
                ListPushIncidentHistory list,
                CancellationToken cancellationToken) =>
            IncidentHistoryResponse(await list.ExecuteAsync(
                http.ActingIdentity(), projectKey, monitorKey, before_opening_sequence, limit, cancellationToken)))
            .WithName("ListPushIncidentHistory")
            .WithSummary("List push incidents newest first with cursor pagination.")
            .Produces<PushIncidentHistoryPageResponse>()
            .Produces<ProblemResponse>(400)
            .Produces<ProblemResponse>(401)
            .Produces<ProblemResponse>(404);

        monitors.MapGet("/{monitorKey}/incidents/{incidentId:guid}", async (
                string projectKey,
                string monitorKey,
                Guid incidentId,
                HttpContext http,
                ReadPushIncidentEvidence read,
                CancellationToken cancellationToken) =>
            IncidentHistoryResponse(await read.ExecuteAsync(
                http.ActingIdentity(), projectKey, monitorKey, incidentId, cancellationToken)))
            .WithName("ReadPushIncidentEvidence")
            .WithSummary("Read one retained push incident in this live monitor.")
            .Produces<PushIncidentHistoryResponse>()
            .Produces<ProblemResponse>(400)
            .Produces<ProblemResponse>(401)
            .Produces<ProblemResponse>(404);

        monitors.MapPut("/{monitorKey}", async (string projectKey, string monitorKey, UpdatePushMonitorRequest request, HttpContext http,
            UpdatePushMonitor update, CancellationToken cancellationToken) => Response(await update.ExecuteAsync(http.ActingIdentity(), projectKey,
                monitorKey, request.Name, request.IntervalSeconds, request.ToleranceSeconds, request.Instruction, request.RunbookUrl, request.Version, cancellationToken, request.Purpose)))
            .WithName("UpdatePushMonitor").WithSummary("Update push monitor configuration at the version last read.").Produces<PushMonitorResponse>().Produces<ProblemResponse>(400).Produces<ProblemResponse>(401).Produces<ProblemResponse>(404).Produces<ProblemResponse>(409);

        monitors.MapPost("/{monitorKey}/pause", async (string projectKey, string monitorKey, PushMonitorVersionRequest request, HttpContext http,
            PausePushMonitor pause, CancellationToken cancellationToken) => Response(await pause.ExecuteAsync(http.ActingIdentity(), projectKey, monitorKey, request.Version, cancellationToken)))
            .WithName("PausePushMonitor").WithSummary("Pause a push monitor.").Produces<PushMonitorResponse>().Produces<ProblemResponse>(400).Produces<ProblemResponse>(401).Produces<ProblemResponse>(404).Produces<ProblemResponse>(409);

        monitors.MapPost("/{monitorKey}/resume", async (string projectKey, string monitorKey, PushMonitorVersionRequest request, HttpContext http,
            ResumePushMonitor resume, CancellationToken cancellationToken) => Response(await resume.ExecuteAsync(http.ActingIdentity(), projectKey, monitorKey, request.Version, cancellationToken)))
            .WithName("ResumePushMonitor").WithSummary("Resume a push monitor with a fresh reporting window.").Produces<PushMonitorResponse>().Produces<ProblemResponse>(400).Produces<ProblemResponse>(401).Produces<ProblemResponse>(404).Produces<ProblemResponse>(409);

        monitors.MapDelete("/{monitorKey}", async (string projectKey, string monitorKey, HttpContext http, RemovePushMonitor remove,
            CancellationToken cancellationToken, string version = "") => Response(await remove.ExecuteAsync(http.ActingIdentity(), projectKey,
                monitorKey, long.TryParse(version, out var parsed) ? parsed : null, cancellationToken)))
            .WithName("RemovePushMonitor").WithSummary("Remove a push monitor while retaining history and its key.").Produces<PushMonitorResponse>().Produces<ProblemResponse>(400).Produces<ProblemResponse>(401).Produces<ProblemResponse>(404).Produces<ProblemResponse>(409);

        monitors.MapPost("/{monitorKey}/reporting-credential", async (string projectKey, string monitorKey, HttpContext http,
            IssueReportingCredential issue, CancellationToken cancellationToken) => Results.Created(
                $"/api/projects/{projectKey}/push-monitors/{monitorKey}/reporting-credential",
                Issued(await issue.ExecuteAsync(http.ActingIdentity(), projectKey, monitorKey, cancellationToken))))
            .WithName("IssueReportingCredential").WithSummary("Issue and reveal a monitor reporting secret once.")
            .Produces<IssuedReportingCredentialResponse>(201).Produces<ProblemResponse>(401).Produces<ProblemResponse>(404).Produces<ProblemResponse>(409);
        monitors.MapGet("/{monitorKey}/reporting-credential", async (string projectKey, string monitorKey, HttpContext http,
            ReadReportingCredential read, CancellationToken cancellationToken) => Metadata(
                await read.ExecuteAsync(http.ActingIdentity(), projectKey, monitorKey, cancellationToken)))
            .WithName("ReadReportingCredential").WithSummary("Read reporting credential metadata without its secret.")
            .Produces<ReportingCredentialResponse>().Produces<ProblemResponse>(401).Produces<ProblemResponse>(404);
        monitors.MapPost("/{monitorKey}/reporting-credential/rotate", async (string projectKey, string monitorKey, HttpContext http,
            RotateReportingCredential rotate, CancellationToken cancellationToken) => Issued(
                await rotate.ExecuteAsync(http.ActingIdentity(), projectKey, monitorKey, cancellationToken)))
            .WithName("RotateReportingCredential").WithSummary("Rotate and reveal a new reporting secret once.")
            .Produces<IssuedReportingCredentialResponse>().Produces<ProblemResponse>(401).Produces<ProblemResponse>(404).Produces<ProblemResponse>(409);
        monitors.MapDelete("/{monitorKey}/reporting-credential", async (string projectKey, string monitorKey, HttpContext http,
            RevokeReportingCredential revoke, CancellationToken cancellationToken) =>
        {
            await revoke.ExecuteAsync(http.ActingIdentity(), projectKey, monitorKey, cancellationToken);
            return Results.NoContent();
        }).WithName("RevokeReportingCredential").WithSummary("Revoke the monitor reporting credential immediately.")
            .Produces(204).Produces<ProblemResponse>(401).Produces<ProblemResponse>(404);
        return endpoints;
    }

    private static PushMonitorResponse Response(PushMonitorSnapshot value) => new(value.Id, value.ProjectKey, value.Key, value.Name,
        value.Mode == Domain.Monitoring.PushMonitorMode.JobCompletion ? "job_completion" : "state_report", value.IntervalSeconds,
        value.ToleranceSeconds, value.Instruction, value.RunbookUrl, value.State.ToString().ToLowerInvariant(), value.LastReceivedAt,
        value.NextDeadlineAt, value.LatestReportId, value.LatestSuccessId, value.OpenIncidentId, value.HasReportingCredential,
        value.Version, value.CreatedAt, value.UpdatedAt, value.PausedAt, value.DeletedAt, value.Purpose);

    private static ReportingCredentialResponse Metadata(ReportingCredentialMetadata value) =>
        new(value.Id, value.CreatedAt, value.RotatedAt, value.RevokedAt);

    private static IssuedReportingCredentialResponse Issued(IssuedReportingCredential value) =>
        new(value.Credential.Id, value.Token, $"/api/report/{value.Token}", value.Credential.CreatedAt,
            value.Credential.RotatedAt, value.PreviousValidUntil);

    private static PushReportHistoryResponse ReportHistoryResponse(PushReportHistoryItem item) => new(
            item.Id,
            item.ReportId,
            item.EvaluationGeneration,
            item.Sequence,
            item.ObservedAt,
            item.ReceivedAt,
            item.Outcome.ToString().ToLowerInvariant(),
            item.Reason,
            item.Applicable);

    private static PushReportHistoryPageResponse ReportHistoryResponse(PushReportHistoryPage value) => new(
        value.Items.Select(ReportHistoryResponse).ToArray(),
        value.NextBeforeSequence);

    private static PushIncidentHistoryResponse IncidentHistoryResponse(PushIncidentHistoryItem item) => new(
            item.Id,
            item.OpeningReportId,
            item.LatestFailureReportId,
            item.ResolutionReportId,
            item.OpeningSequence,
            item.LatestFailureSequence,
            item.ResolutionSequence,
            item.BeganAt,
            item.OpenedAt,
            item.LastObservedAt,
            item.ResolvedAt,
            item.OriginalReason,
            item.LatestReason);

    private static PushIncidentHistoryPageResponse IncidentHistoryResponse(PushIncidentHistoryPage value) => new(
        value.Items.Select(IncidentHistoryResponse).ToArray(),
        value.NextBeforeOpeningSequence);
}
