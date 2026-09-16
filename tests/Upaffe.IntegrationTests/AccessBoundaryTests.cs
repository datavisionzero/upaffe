using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Upaffe.Api.Http;
using Upaffe.Application.Access;
using Upaffe.Domain.Access;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class AccessBoundaryTests(PostgresFixture postgres)
{
    private const string BootstrapProof = "an-access-boundary-bootstrap-proof-with-enough-entropy";
    private const string Email = "operator@example.test";
    private const string Password = "a long operator password";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public async Task Every_route_declares_one_boundary_and_unclassified_routes_are_closed()
    {
        await using var instance = await EstablishedAsync();
        using var client = instance.CreateClient();

        // Materialize the host and its endpoint data sources.
        using var response = await client.GetAsync("/api/version", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var routes = instance.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        Assert.NotEmpty(routes);
        foreach (var route in routes)
        {
            Assert.Single(route.Metadata.OfType<AccessBoundary>());
        }

        AssertBoundary(routes, "/api/version", "GET", AccessBoundary.Public);
        AssertBoundary(routes, "/api/bootstrap", "POST", AccessBoundary.Public);
        AssertBoundary(routes, "/api/session", "POST", AccessBoundary.Public);
        AssertBoundary(routes, "/api/session", "GET", AccessBoundary.Browser);
        AssertBoundary(routes, "/api/session", "DELETE", AccessBoundary.Browser);
        AssertBoundary(routes, "/api/management-credentials", "GET", AccessBoundary.Management);
        AssertBoundary(routes, "/api/management-credentials", "POST", AccessBoundary.Management);
        AssertBoundary(routes, "/api/projects", "GET", AccessBoundary.Management);
        AssertBoundary(routes, "/api/projects", "POST", AccessBoundary.Management);

        var policies = instance.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var fallback = await policies.GetFallbackPolicyAsync();
        Assert.NotNull(fallback);
        Assert.Contains(BrowserAuthentication.Scheme, fallback.AuthenticationSchemes);
        Assert.Contains(fallback.Requirements, requirement =>
            requirement is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task Anonymous_browser_and_management_paths_have_a_stable_access_matrix()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        await using var instance = await EstablishedAsync(clock);
        using var client = Client(instance);

        Assert.Equal(HttpStatusCode.OK, await Status(client, HttpMethod.Get, "/api/version"));
        Assert.Equal(
            "authentication_required",
            await ProblemCode(client, HttpMethod.Get, "/api/session"));
        Assert.Equal(
            "authentication_required",
            await ProblemCode(client, HttpMethod.Get, "/api/management-credentials"));

        var browser = await SignInAsync(client);
        Assert.Equal(
            HttpStatusCode.OK,
            await Status(client, HttpMethod.Get, "/api/session", cookie: browser));
        Assert.Equal(
            HttpStatusCode.OK,
            await Status(client, HttpMethod.Get, "/api/management-credentials", cookie: browser));

        var issued = await CreateCredentialAsync(client, browser);
        Assert.Equal(
            HttpStatusCode.OK,
            await Status(client, HttpMethod.Get, "/api/management-credentials", bearer: issued.Token));
        Assert.Equal(
            "forbidden",
            await ProblemCode(client, HttpMethod.Get, "/api/session", bearer: issued.Token));

        using (var revoked = Request(
            HttpMethod.Delete,
            $"/api/management-credentials/{issued.Id}",
            cookie: browser,
            csrf: true))
        using (var response = await client.SendAsync(revoked, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        Assert.Equal(
            "authentication_rejected",
            await ProblemCode(
                client,
                HttpMethod.Get,
                "/api/management-credentials",
                bearer: issued.Token));

        clock.Advance(BrowserSession.IdleLifetime);
        Assert.Equal(
            "authentication_rejected",
            await ProblemCode(client, HttpMethod.Get, "/api/session", cookie: browser));
    }

    [Fact]
    public async Task Security_logs_name_the_operation_and_access_id_without_secrets()
    {
        var logs = new CollectingLoggerProvider();
        await using var instance = await EstablishedAsync(logs: logs);
        using var client = Client(instance);
        var browser = await SignInAsync(client);
        var issued = await CreateCredentialAsync(client, browser);

        Assert.Equal(
            HttpStatusCode.OK,
            await Status(client, HttpMethod.Get, "/api/management-credentials", bearer: issued.Token));

        Assert.Contains(logs.Messages, message =>
            message.Contains("Authentication admitted GET /api/management-credentials", StringComparison.Ordinal)
            && message.Contains(issued.Id.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Messages, message =>
            message.Contains(browser, StringComparison.Ordinal)
            || message.Contains(issued.Token, StringComparison.Ordinal)
            || message.Contains(Password, StringComparison.Ordinal));
    }

    private async Task<AnInstance> EstablishedAsync(
        MutableTimeProvider? clock = null,
        CollectingLoggerProvider? logs = null)
    {
        var instance = AnInstance.Against(
            await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?> { [BootstrapSettings.Variable] = BootstrapProof },
            clock,
            logs);
        using var client = instance.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/bootstrap",
            new BootstrapRequest(BootstrapProof, Email, Password),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return instance;
    }

    private static HttpClient Client(AnInstance instance) =>
        instance.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static async Task<string> SignInAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/session",
            new SignInRequest(Email, Password),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';', 2)[0].Split('=', 2)[1];
    }

    private static async Task<IssuedCredentialResponse> CreateCredentialAsync(
        HttpClient client,
        string browser)
    {
        using var request = Request(
            HttpMethod.Post,
            "/api/management-credentials",
            cookie: browser,
            csrf: true);
        request.Content = JsonContent.Create(new CreateCredentialRequest("boundary agent"));
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IssuedCredentialResponse>(
            Json,
            TestContext.Current.CancellationToken))!;
    }

    private static async Task<HttpStatusCode> Status(
        HttpClient client,
        HttpMethod method,
        string path,
        string? bearer = null,
        string? cookie = null)
    {
        using var request = Request(method, path, bearer, cookie);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private static async Task<string?> ProblemCode(
        HttpClient client,
        HttpMethod method,
        string path,
        string? bearer = null,
        string? cookie = null)
    {
        using var request = Request(method, path, bearer, cookie);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        return (await response.Content.ReadFromJsonAsync<ProblemResponse>(
            TestContext.Current.CancellationToken))?.Code;
    }

    private static HttpRequestMessage Request(
        HttpMethod method,
        string path,
        string? bearer = null,
        string? cookie = null,
        bool csrf = false)
    {
        var request = new HttpRequestMessage(method, path);
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", BrowserCookie.PlainName + "=" + cookie);
        }

        if (csrf)
        {
            request.Headers.Add(CsrfProtection.Header, "1");
            request.Headers.Add("Origin", "http://localhost");
        }

        return request;
    }

    private static void AssertBoundary(
        IEnumerable<RouteEndpoint> endpoints,
        string pattern,
        string method,
        AccessBoundary expected)
    {
        var endpoint = Assert.Single(endpoints, candidate =>
            candidate.RoutePattern.RawText?.TrimEnd('/') == pattern.TrimEnd('/')
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) == true);
        Assert.Equal(expected, Assert.Single(endpoint.Metadata.OfType<AccessBoundary>()));
    }
}
