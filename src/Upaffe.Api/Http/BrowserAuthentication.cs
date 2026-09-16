using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;

namespace Upaffe.Api.Http;

public static class BrowserAuthentication
{
    public const string Scheme = "UpaffeAccess";
    public const string Policy = "browser";
    public const string ManagementPolicy = "management";
    internal const string RejectedItem = "upaffe.authentication_rejected";

    public static IServiceCollection AddBrowserAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, BrowserAuthenticationHandler>(
                Scheme,
                displayName: null,
                configureOptions: null);
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(Scheme)
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(Policy, policy => policy
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim("access_path", "browser_session"))
            .AddPolicy(ManagementPolicy, policy => policy
                .AddAuthenticationSchemes(Scheme)
                .RequireAuthenticatedUser());
        services.AddSingleton<LoginThrottle>();
        return services;
    }
}

public sealed class BrowserAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggers,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggers, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Public operations do not consume or extend a session merely because
        // the browser attached its path-wide cookie.
        if (Context.GetEndpoint()?.Metadata.GetMetadata<IAuthorizeData>() is null)
        {
            return AuthenticateResult.NoResult();
        }

        if (Request.Headers.Authorization.ToString() is { Length: > 0 } authorization)
        {
            return await AuthenticateManagementAsync(authorization);
        }

        if (!Request.Cookies.TryGetValue(BrowserCookie.For(Request).Name, out var secret))
        {
            return AuthenticateResult.NoResult();
        }

        if (!LooksLikeSecret(secret))
        {
            Context.Items[BrowserAuthentication.RejectedItem] = true;
            return AuthenticateResult.Fail("The presented session was rejected.");
        }

        var sessions = Context.RequestServices.GetRequiredService<IBrowserSessionStore>();
        var clock = Context.RequestServices.GetRequiredService<TimeProvider>();
        var admitted = await sessions.AdmitAsync(
            SecretValue.Hash(secret),
            clock.GetUtcNow(),
            Context.RequestAborted);
        if (admitted is null)
        {
            Context.Items[BrowserAuthentication.RejectedItem] = true;
            return AuthenticateResult.Fail("The presented session was rejected.");
        }

        Context.Features.Set(admitted);
        Context.Features.Set(admitted.Identity);
        AuditAdmitted(admitted.Identity);
        return Success(admitted.Identity);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var rejected = Context.Items.ContainsKey(BrowserAuthentication.RejectedItem);
        Logger.LogInformation(
            "Authentication {Outcome} for {Method} {Path}.",
            rejected ? "rejected" : "required",
            Request.Method,
            Request.Path);
        throw rejected ? Refusal.AuthenticationRejected() : Refusal.AuthenticationRequired();
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        var identity = Context.Features.Get<Identity>();
        Logger.LogInformation(
            "Authentication forbidden for {Method} {Path} via {AccessPath} {AccessId}.",
            Request.Method,
            Request.Path,
            identity?.Path.ToString() ?? "unknown",
            identity?.AccessId.ToString() ?? "unknown");
        throw Refusal.Forbidden();
    }

    private async Task<AuthenticateResult> AuthenticateManagementAsync(string authorization)
    {
        const string prefix = "Bearer ";
        var token = authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[prefix.Length..]
            : string.Empty;
        if (token.Contains(' ', StringComparison.Ordinal)
            || !ManagementToken.TryParse(token, out var credentialId, out var secretHash))
        {
            Context.Items[BrowserAuthentication.RejectedItem] = true;
            return AuthenticateResult.Fail("The presented credential was rejected.");
        }

        var credentials = Context.RequestServices.GetRequiredService<IManagementCredentialStore>();
        var clock = Context.RequestServices.GetRequiredService<TimeProvider>();
        var admitted = await credentials.AdmitAsync(
            credentialId,
            secretHash,
            clock.GetUtcNow(),
            Context.RequestAborted);
        if (admitted is null)
        {
            Context.Items[BrowserAuthentication.RejectedItem] = true;
            return AuthenticateResult.Fail("The presented credential was rejected.");
        }

        Context.Features.Set(admitted);
        Context.Features.Set(admitted.Identity);
        AuditAdmitted(admitted.Identity);
        return Success(admitted.Identity);
    }

    private void AuditAdmitted(Identity identity) => Logger.LogInformation(
        "Authentication admitted {Method} {Path} via {AccessPath} {AccessId}.",
        Request.Method,
        Request.Path,
        identity.Path,
        identity.AccessId);

    private AuthenticateResult Success(Identity identity)
    {
        var path = identity.Path == AccessPath.BrowserSession
            ? "browser_session"
            : "management_credential";
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, identity.OperatorId.ToString()),
                new Claim("access_path", path),
            ],
            Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    private static bool LooksLikeSecret(string value) =>
        value.Length == 43
        && value.All(character =>
            character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '-' or '_');
}
