using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Upaffe.Api.Http;
using Upaffe.Application.Access;
using Upaffe.Domain.Access;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class ManagementCredentialTests(PostgresFixture postgres)
{
    private const string Email = "operator@example.test";
    private const string Password = "a long operator password";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public async Task Create_reveals_the_token_once_while_list_and_storage_keep_only_metadata()
    {
        var logs = new CollectingLoggerProvider();
        var (instance, connection) = await EstablishedAsync(logs: logs);
        await using (instance)
        using (var client = Client(instance))
        {
            var browser = await BrowserSessionAsync(client);
            using var created = await CreateAsync(client, browser, "deployment agent");
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var issued = await created.Content.ReadFromJsonAsync<IssuedCredentialResponse>(
                Json,
                TestContext.Current.CancellationToken);
            Assert.NotNull(issued);
            Assert.True(ManagementToken.TryParse(issued.Token, out var tokenId, out var tokenHash));
            Assert.Equal(issued.Id, tokenId);

            using var listed = await AuthorizedAsync(
                client,
                HttpMethod.Get,
                "/api/management-credentials",
                cookie: browser);
            var listBody = await listed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            Assert.DoesNotContain(issued.Token, listBody, StringComparison.Ordinal);
            Assert.DoesNotContain(issued.Token[(issued.Token.LastIndexOf('_') + 1)..], listBody, StringComparison.Ordinal);
            var rows = JsonSerializer.Deserialize<CredentialResponse[]>(listBody, Json);
            Assert.Equal(2, rows!.Length);
            var metadata = rows.Single(row => row.Name == "deployment agent");
            Assert.Equal("deployment agent", metadata.Name);

            using var bearerList = await AuthorizedAsync(
                client,
                HttpMethod.Get,
                "/api/management-credentials",
                bearer: issued.Token);
            Assert.Equal(HttpStatusCode.OK, bearerList.StatusCode);

            await using var context = AnInstance.ContextFor(connection);
            var stored = await context.ManagementCredentialSecrets.SingleAsync(
                value => value.CredentialId == issued.Id,
                TestContext.Current.CancellationToken);
            Assert.Equal(tokenHash, stored.SecretHash);
            Assert.DoesNotContain(logs.Messages, message => message.Contains(issued.Token, StringComparison.Ordinal));

            using var duplicate = await CreateAsync(client, browser, "deployment agent");
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal("conflict", await Code(duplicate));
        }
    }

    [Fact]
    public async Task A_credential_can_administer_credentials_but_cannot_act_as_the_browser()
    {
        var (instance, _) = await EstablishedAsync();
        await using (instance)
        using (var client = Client(instance))
        {
            var browser = await BrowserSessionAsync(client);
            var first = await IssuedAsync(await CreateAsync(client, browser, "first agent"));

            using var created = await AuthorizedJsonAsync(
                client,
                HttpMethod.Post,
                "/api/management-credentials",
                new CreateCredentialRequest("second agent"),
                bearer: first.Token);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            using var listed = await AuthorizedAsync(
                client,
                HttpMethod.Get,
                "/api/management-credentials",
                bearer: first.Token);
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);

            using var signOut = await AuthorizedAsync(
                client,
                HttpMethod.Delete,
                "/api/session",
                bearer: first.Token);
            Assert.Equal(HttpStatusCode.Forbidden, signOut.StatusCode);
            Assert.Equal("forbidden", await Code(signOut));
        }
    }

    [Fact]
    public async Task Rotation_overlaps_for_ten_minutes_and_revocation_is_immediate()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var (instance, _) = await EstablishedAsync(clock);
        await using (instance)
        using (var client = Client(instance))
        {
            var browser = await BrowserSessionAsync(client);
            var original = await IssuedAsync(await CreateAsync(client, browser, "rotating agent"));

            using var rotatedResponse = await AuthorizedAsync(
                client,
                HttpMethod.Post,
                $"/api/management-credentials/{original.Id}/rotate",
                bearer: original.Token);
            Assert.Equal(HttpStatusCode.OK, rotatedResponse.StatusCode);
            var rotated = await IssuedAsync(rotatedResponse);
            Assert.NotEqual(original.Token, rotated.Token);
            Assert.Equal(clock.GetUtcNow().AddMinutes(10), rotated.PreviousValidUntil);

            Assert.Equal(HttpStatusCode.OK, await ListStatus(client, original.Token));
            Assert.Equal(HttpStatusCode.OK, await ListStatus(client, rotated.Token));

            clock.Advance(ManagementCredentialSecret.RotationOverlap);
            Assert.Equal("authentication_rejected", await ListRejection(client, original.Token));
            Assert.Equal(HttpStatusCode.OK, await ListStatus(client, rotated.Token));

            using var revoked = await AuthorizedAsync(
                client,
                HttpMethod.Delete,
                $"/api/management-credentials/{original.Id}",
                bearer: rotated.Token);
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
            Assert.Equal("authentication_rejected", await ListRejection(client, rotated.Token));

            using var idempotent = await AuthorizedAsync(
                client,
                HttpMethod.Delete,
                $"/api/management-credentials/{original.Id}",
                cookie: browser,
                csrf: true);
            Assert.Equal(HttpStatusCode.NoContent, idempotent.StatusCode);
        }
    }

    [Fact]
    public async Task Missing_malformed_unknown_and_revoked_authentication_have_stable_codes()
    {
        var (instance, _) = await EstablishedAsync();
        await using (instance)
        using (var client = Client(instance))
        {
            Assert.Equal("authentication_required", await ListRejection(client, bearer: null));
            Assert.Equal("authentication_rejected", await ListRejection(client, "not-a-token"));
            var unknown = ManagementToken.Format(Guid.NewGuid(), SecretValue.Create().Secret);
            Assert.Equal("authentication_rejected", await ListRejection(client, unknown));

            var browser = await BrowserSessionAsync(client);
            var issued = await IssuedAsync(await CreateAsync(client, browser, "revoked agent"));
            using var revoked = await AuthorizedAsync(
                client,
                HttpMethod.Delete,
                $"/api/management-credentials/{issued.Id}",
                cookie: browser,
                csrf: true);
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
            Assert.Equal("authentication_rejected", await ListRejection(client, issued.Token));
        }
    }

    private async Task<(AnInstance Instance, string Connection)> EstablishedAsync(
        MutableTimeProvider? clock = null,
        CollectingLoggerProvider? logs = null)
    {
        var connection = await postgres.CreateDatabaseAsync();
        var instance = AnInstance.Against(connection, clock: clock, logProvider: logs);
        using var client = instance.CreateClient();
        await instance.EstablishAsync(Email, Password);
        return (instance, connection);
    }

    private static HttpClient Client(AnInstance instance) =>
        instance.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static async Task<string> BrowserSessionAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/session",
            new SignInRequest(Email, Password),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';', 2)[0].Split('=', 2)[1];
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string cookie, string name) =>
        AuthorizedJsonAsync(
            client,
            HttpMethod.Post,
            "/api/management-credentials",
            new CreateCredentialRequest(name),
            cookie: cookie,
            csrf: true);

    private static async Task<IssuedCredentialResponse> IssuedAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<IssuedCredentialResponse>(
            Json,
            TestContext.Current.CancellationToken))!;

    private static async Task<HttpStatusCode> ListStatus(HttpClient client, string bearer)
    {
        using var response = await AuthorizedAsync(
            client,
            HttpMethod.Get,
            "/api/management-credentials",
            bearer: bearer);
        return response.StatusCode;
    }

    private static async Task<string?> ListRejection(HttpClient client, string? bearer)
    {
        using var response = await AuthorizedAsync(
            client,
            HttpMethod.Get,
            "/api/management-credentials",
            bearer: bearer,
            forceAuthorization: bearer is not null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        return await Code(response);
    }

    private static async Task<string?> Code(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemResponse>(
            TestContext.Current.CancellationToken))?.Code;

    private static Task<HttpResponseMessage> AuthorizedJsonAsync<T>(
        HttpClient client,
        HttpMethod method,
        string path,
        T body,
        string? bearer = null,
        string? cookie = null,
        bool csrf = false)
    {
        var request = Request(method, path, bearer, cookie, csrf, bearer is not null);
        request.Content = JsonContent.Create(body);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> AuthorizedAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? bearer = null,
        string? cookie = null,
        bool csrf = false,
        bool forceAuthorization = false) =>
        client.SendAsync(
            Request(method, path, bearer, cookie, csrf, forceAuthorization),
            TestContext.Current.CancellationToken);

    private static HttpRequestMessage Request(
        HttpMethod method,
        string path,
        string? bearer,
        string? cookie,
        bool csrf,
        bool forceAuthorization)
    {
        var request = new HttpRequestMessage(method, path);
        if (bearer is not null || forceAuthorization)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer ?? string.Empty);
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
}
