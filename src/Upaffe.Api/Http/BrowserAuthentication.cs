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
    public const string Scheme = "UpaffeBrowser";
    public const string Policy = "browser";
    internal const string RejectedItem = "upaffe.authentication_rejected";

    public static IServiceCollection AddBrowserAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, BrowserAuthenticationHandler>(
                Scheme,
                displayName: null,
                configureOptions: null);
        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, policy => policy
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
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, admitted.Identity.OperatorId.ToString())],
            Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        throw (Context.Items.ContainsKey(BrowserAuthentication.RejectedItem)
            ? Refusal.AuthenticationRejected()
            : Refusal.AuthenticationRequired());

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        throw Refusal.Forbidden();

    private static bool LooksLikeSecret(string value) =>
        value.Length == 43
        && value.All(character =>
            character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '-' or '_');
}
