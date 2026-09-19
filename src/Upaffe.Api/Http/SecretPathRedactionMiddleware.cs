namespace Upaffe.Api.Http;

/// <summary>Removes a reporting credential from the path before routing and application logging.</summary>
public sealed class SecretPathRedactionMiddleware(RequestDelegate next)
{
    public const string SecretItem = "upaffe.reporting-secret";
    public const string RedactedPath = "/api/report/{redacted}";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api/report", out var remaining)
            && remaining.HasValue
            && remaining.Value!.Length > 1
            && remaining.Value[0] == '/')
        {
            if (!remaining.Value.AsSpan(1).Contains('/'))
            {
                context.Items[SecretItem] = remaining.Value[1..];
            }

            context.Request.Path = RedactedPath;
        }

        await next(context);
    }
}
