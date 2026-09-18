using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Upaffe.Application.Monitoring;

namespace Upaffe.Api.Http;

public sealed record SubmitPushReportRequest(
    Guid? ReportId,
    DateTimeOffset? ObservedAt,
    string? Outcome,
    string? Reason);

public sealed record PushReportReceiptResponse(
    Guid ReportId,
    DateTimeOffset ReceivedAt,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)] long Sequence,
    bool Applied,
    bool Duplicate);

public static class PushReportEndpoints
{
    public static IEndpointRouteBuilder MapPushReports(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/report/{secret}", Simple)
            .PublicAccess()
            .WithName("SubmitSimplePushReportGet")
            .WithSummary("Submit a success through a secret monitor URL using GET.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");
        endpoints.MapPost("/report/{secret}", Simple)
            .PublicAccess()
            .WithName("SubmitSimplePushReportPost")
            .WithSummary("Submit a success through a secret monitor URL using POST.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json");

        endpoints.MapPost("/reports", async (
                SubmitPushReportRequest request,
                HttpContext http,
                SubmitPushReport submit,
                CancellationToken cancellationToken) =>
            {
                var result = await submit.ExecuteAsync(
                    Bearer(http.Request.Headers.Authorization),
                    request.ReportId,
                    request.ObservedAt,
                    request.Outcome,
                    request.Reason,
                    cancellationToken);
                return Results.Accepted(value: new PushReportReceiptResponse(
                    result.ReportId,
                    result.ReceivedAt,
                    result.Sequence,
                    result.Applied,
                    result.Duplicate));
            })
            .PublicAccess()
            .WithName("SubmitPushReport")
            .WithSummary("Submit an idempotent success or failure to one push monitor.")
            .Produces<PushReportReceiptResponse>(StatusCodes.Status202Accepted)
            .Produces<ProblemResponse>(StatusCodes.Status400BadRequest, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status409Conflict, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status422UnprocessableEntity, "application/problem+json");
        return endpoints;
    }

    private static async Task<IResult> Simple(
        string secret,
        HttpContext http,
        SubmitSimplePushReport submit,
        CancellationToken cancellationToken)
    {
        _ = secret;
        await submit.ExecuteAsync(
            http.Items[SecretPathRedactionMiddleware.SecretItem] as string,
            cancellationToken);
        return Results.NoContent();
    }

    private static string? Bearer(string? value) =>
        AuthenticationHeaderValue.TryParse(value, out var header)
            && string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            ? header.Parameter
            : null;
}
