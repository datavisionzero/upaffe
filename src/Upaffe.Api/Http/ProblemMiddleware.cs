using System.Text.Json.Serialization;
using Upaffe.Application.Failures;

namespace Upaffe.Api.Http;

public sealed record ProblemResponse(
    string Code,
    string Title,
    [property: JsonNumberHandling(JsonNumberHandling.Strict)]
    int Status,
    IReadOnlyDictionary<string, string[]>? Errors = null);

/// <summary>Turns expected application refusals into one stable HTTP shape.</summary>
public sealed class ProblemMiddleware(RequestDelegate next, ILogger<ProblemMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Refusal refusal)
        {
            var status = refusal.Code switch
            {
                "validation" => StatusCodes.Status400BadRequest,
                "bootstrap_rejected" => StatusCodes.Status401Unauthorized,
                "bootstrap_closed" => StatusCodes.Status409Conflict,
                "sign_in_rejected" => StatusCodes.Status401Unauthorized,
                "authentication_required" => StatusCodes.Status401Unauthorized,
                "authentication_rejected" => StatusCodes.Status401Unauthorized,
                "forbidden" => StatusCodes.Status403Forbidden,
                "conflict" => StatusCodes.Status409Conflict,
                "not_found" => StatusCodes.Status404NotFound,
                "reporting_rejected" => StatusCodes.Status401Unauthorized,
                "report_id_conflict" => StatusCodes.Status409Conflict,
                "unprocessable" => StatusCodes.Status422UnprocessableEntity,
                _ => StatusCodes.Status400BadRequest,
            };
            logger.LogInformation(
                "{Method} {Path} was refused with {Code}.",
                context.Request.Method,
                RedactedPath(context.Request.Path),
                refusal.Code);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(
                new ProblemResponse(refusal.Code, refusal.Message, status, refusal.Errors),
                context.RequestAborted);
        }
    }

    private static string RedactedPath(PathString path) =>
        path.StartsWithSegments("/api/report")
            ? SecretPathRedactionMiddleware.RedactedPath
            : path.Value ?? string.Empty;
}
