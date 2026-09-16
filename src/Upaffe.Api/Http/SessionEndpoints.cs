using System.Security.Cryptography;
using System.Text;
using Upaffe.Application.Access;
using Upaffe.Application.Failures;
using Upaffe.Application.Ports;

namespace Upaffe.Api.Http;

public sealed record SignInRequest(string? Email, string? Password);

public sealed record CurrentSessionResponse(
    Guid OperatorId,
    string Email,
    string AccessPath,
    Guid SessionId,
    DateTimeOffset ExpiresAt);

/// <summary>The operator's browser entry, identity inspection, and exit.</summary>
public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSession(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/session", async (
                SignInRequest? request,
                HttpContext http,
                SignIn signIn,
                LoginThrottle throttle,
                CancellationToken cancellationToken) =>
            {
                var account = Folded(request?.Email);
                var source = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                if (throttle.IsBlocked(account, source))
                {
                    throw Refusal.SignInRejected();
                }

                IssuedBrowserSession issued;
                try
                {
                    issued = await signIn.ExecuteAsync(
                        request?.Email,
                        request?.Password,
                        http.Request.Headers.UserAgent.ToString(),
                        cancellationToken);
                }
                catch (Refusal refusal) when (refusal.Code == "sign_in_rejected")
                {
                    throttle.Failed(account, source);
                    throw;
                }

                throttle.Succeeded(account);
                var cookie = BrowserCookie.For(http.Request);
                http.Response.Cookies.Append(
                    cookie.Name,
                    issued.Secret,
                    cookie.Options(issued.Session.ExpiresAt));
                return Results.NoContent();
            })
            .AllowAnonymous()
            .WithName("SignIn")
            .WithSummary("Sign in as the operator; the new session is returned as an HttpOnly cookie.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json");

        endpoints.MapGet("/session", (HttpContext http) =>
            {
                var admitted = Current(http);
                return Results.Ok(new CurrentSessionResponse(
                    admitted.Identity.OperatorId,
                    admitted.Email,
                    "browser_session",
                    admitted.Identity.AccessId,
                    admitted.ExpiresAt));
            })
            .RequireAuthorization(BrowserAuthentication.Policy)
            .WithName("ReadCurrentSession")
            .WithSummary("Inspect the operator identity admitted by the current browser session.")
            .Produces<CurrentSessionResponse>()
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json");

        endpoints.MapDelete("/session", async (
                HttpContext http,
                SignOut signOut,
                CancellationToken cancellationToken) =>
            {
                await signOut.ExecuteAsync(Current(http).Identity, cancellationToken);
                BrowserCookie.Forget(http.Response);
                return Results.NoContent();
            })
            .RequireAuthorization(BrowserAuthentication.Policy)
            .WithName("SignOut")
            .WithSummary("Revoke and forget the current browser session.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemResponse>(StatusCodes.Status401Unauthorized, "application/problem+json")
            .Produces<ProblemResponse>(StatusCodes.Status403Forbidden, "application/problem+json");

        return endpoints;
    }

    private static AdmittedBrowser Current(HttpContext context) =>
        context.Features.Get<AdmittedBrowser>()
        ?? throw Refusal.AuthenticationRequired();

    private static string Folded(string? email)
    {
        var normalized = email?.Trim().ToUpperInvariant() ?? string.Empty;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
