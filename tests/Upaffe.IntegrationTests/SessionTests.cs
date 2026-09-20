using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Upaffe.Api.Http;
using Upaffe.Application.Access;
using Upaffe.Domain.Access;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class SessionTests(PostgresFixture postgres)
{
    private const string Email = "operator@example.test";
    private const string Password = "a long operator password";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public async Task Sign_in_issues_a_fresh_plain_http_cookie_and_identifies_the_operator()
    {
        var (instance, connection) = await EstablishedAsync();
        await using (instance)
        using (var client = instance.CreateClient())
        {
            client.DefaultRequestHeaders.Add(
                "Cookie",
                BrowserCookie.PlainName + "=" + new string('A', 43));
            using var response = await client.PostAsJsonAsync(
                "/api/session",
                new SignInRequest(" Operator@Example.Test ", Password),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
            Assert.Contains(BrowserCookie.PlainName + "=", setCookie, StringComparison.Ordinal);
            Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("; secure", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(new string('A', 43), CookieValue(setCookie));

            client.DefaultRequestHeaders.Remove("Cookie");
            client.DefaultRequestHeaders.Add(
                "Cookie",
                BrowserCookie.PlainName + "=" + CookieValue(setCookie));
            var current = await client.GetFromJsonAsync<CurrentSessionResponse>(
                "/api/session",
                Json,
                TestContext.Current.CancellationToken);
            Assert.Equal(Email, current?.Email);
            Assert.Equal("browser_session", current?.AccessPath);

            await using var context = AnInstance.ContextFor(connection);
            var stored = await context.BrowserSessions.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(SecretValue.Hash(CookieValue(setCookie)), stored.SecretHash);
        }
    }

    [Fact]
    public async Task Https_uses_the_secure_host_cookie()
    {
        var (instance, _) = await EstablishedAsync();
        await using (instance)
        using (var client = instance.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        }))
        using (var response = await client.PostAsJsonAsync(
            "/api/session",
            new SignInRequest(Email, Password),
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
            Assert.Contains(BrowserCookie.SecureName + "=", setCookie, StringComparison.Ordinal);
            Assert.Contains("; secure", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Missing_manipulated_expired_and_revoked_sessions_have_bounded_answers()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var (instance, connection) = await EstablishedAsync(clock);
        await using (instance)
        using (var client = instance.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }))
        {
            Assert.Equal("authentication_required", await RejectionCode(client, cookie: null));
            Assert.Equal("authentication_rejected", await RejectionCode(client, new string('A', 43)));

            var issued = await SignInAsync(client);
            clock.Advance(BrowserSession.IdleLifetime);
            Assert.Equal("authentication_rejected", await RejectionCode(client, issued));

            clock.Advance(TimeSpan.FromTicks(1));
            var live = await SignInAsync(client);
            using var signOut = new HttpRequestMessage(HttpMethod.Delete, "/api/session");
            signOut.Headers.Add("Cookie", BrowserCookie.PlainName + "=" + live);
            signOut.Headers.Add(CsrfProtection.Header, "1");
            signOut.Headers.Add("Origin", "http://localhost");
            using var signedOut = await client.SendAsync(signOut, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, signedOut.StatusCode);
            Assert.Equal("authentication_rejected", await RejectionCode(client, live));

            await using var context = AnInstance.ContextFor(connection);
            Assert.Equal(1, await context.BrowserSessions.CountAsync(
                value => value.RevokedAt != null,
                TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Sign_out_requires_same_origin_csrf_and_expires_both_cookie_names()
    {
        var (instance, connection) = await EstablishedAsync();
        await using (instance)
        using (var client = instance.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }))
        {
            var secret = await SignInAsync(client);
            using var unproved = new HttpRequestMessage(HttpMethod.Delete, "/api/session");
            unproved.Headers.Add("Cookie", BrowserCookie.PlainName + "=" + secret);
            using var refused = await client.SendAsync(unproved, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal(
                "forbidden",
                (await refused.Content.ReadFromJsonAsync<ProblemResponse>(TestContext.Current.CancellationToken))?.Code);

            using var proved = new HttpRequestMessage(HttpMethod.Delete, "/api/session");
            proved.Headers.Add("Cookie", BrowserCookie.PlainName + "=" + secret);
            proved.Headers.Add(CsrfProtection.Header, "1");
            proved.Headers.Add("Origin", "http://localhost");
            using var success = await client.SendAsync(proved, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
            var forgotten = success.Headers.GetValues("Set-Cookie").ToArray();
            Assert.Contains(forgotten, value => value.StartsWith(BrowserCookie.PlainName + "=", StringComparison.Ordinal));
            Assert.Contains(forgotten, value => value.StartsWith(BrowserCookie.SecureName + "=", StringComparison.Ordinal));

            await using var context = AnInstance.ContextFor(connection);
            Assert.NotNull((await context.BrowserSessions.SingleAsync(
                TestContext.Current.CancellationToken)).RevokedAt);
        }
    }

    [Fact]
    public async Task Failed_sign_ins_are_bounded_per_account_and_the_window_reopens()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var (instance, _) = await EstablishedAsync(clock);
        await using (instance)
        using (var client = instance.CreateClient())
        {
            for (var attempt = 0; attempt < LoginThrottle.AccountLimit; attempt++)
            {
                using var wrong = await client.PostAsJsonAsync(
                    "/api/session",
                    new SignInRequest(Email, "this password is wrong"),
                    TestContext.Current.CancellationToken);
                Assert.Equal("sign_in_rejected", await Code(wrong));
            }

            using var blocked = await client.PostAsJsonAsync(
                "/api/session",
                new SignInRequest(Email, Password),
                TestContext.Current.CancellationToken);
            Assert.Equal("sign_in_rejected", await Code(blocked));

            clock.Advance(LoginThrottle.Window.Add(TimeSpan.FromTicks(1)));
            using var accepted = await client.PostAsJsonAsync(
                "/api/session",
                new SignInRequest(Email, Password),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        }
    }

    [Fact]
    public async Task A_live_session_is_touched_at_most_every_five_minutes()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));
        var (instance, connection) = await EstablishedAsync(clock);
        await using (instance)
        using (var client = instance.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }))
        {
            var secret = await SignInAsync(client);
            clock.Advance(TimeSpan.FromMinutes(4));
            Assert.Equal(HttpStatusCode.OK, await GetCurrentStatus(client, secret));
            await using (var context = AnInstance.ContextFor(connection))
            {
                Assert.Equal(
                    new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero),
                    (await context.BrowserSessions.SingleAsync(TestContext.Current.CancellationToken)).LastUsedAt);
            }

            clock.Advance(TimeSpan.FromMinutes(2));
            using (var publicRequest = new HttpRequestMessage(HttpMethod.Get, "/api/version"))
            {
                publicRequest.Headers.Add("Cookie", BrowserCookie.PlainName + "=" + secret);
                using var publicResponse = await client.SendAsync(
                    publicRequest,
                    TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
            }
            await using (var untouched = AnInstance.ContextFor(connection))
            {
                Assert.Equal(
                    new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero),
                    (await untouched.BrowserSessions.SingleAsync(
                        TestContext.Current.CancellationToken)).LastUsedAt);
            }

            Assert.Equal(HttpStatusCode.OK, await GetCurrentStatus(client, secret));
            await using var touched = AnInstance.ContextFor(connection);
            Assert.Equal(
                clock.GetUtcNow(),
                (await touched.BrowserSessions.SingleAsync(TestContext.Current.CancellationToken)).LastUsedAt);
        }
    }

    [Fact]
    public async Task Password_and_session_secret_are_absent_from_responses_and_logs()
    {
        var logs = new CollectingLoggerProvider();
        var (instance, _) = await EstablishedAsync(logs: logs);
        await using (instance)
        using (var client = instance.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }))
        {
            var secret = await SignInAsync(client);
            Assert.Equal(HttpStatusCode.OK, await GetCurrentStatus(client, secret));

            Assert.DoesNotContain(logs.Messages, message => message.Contains(Password, StringComparison.Ordinal));
            Assert.DoesNotContain(logs.Messages, message => message.Contains(secret, StringComparison.Ordinal));
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

    private static async Task<string> SignInAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/session",
            new SignInRequest(Email, Password),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return CookieValue(Assert.Single(response.Headers.GetValues("Set-Cookie")));
    }

    private static async Task<string?> RejectionCode(HttpClient client, string? cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/session");
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", BrowserCookie.PlainName + "=" + cookie);
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        return await Code(response);
    }

    private static async Task<HttpStatusCode> GetCurrentStatus(HttpClient client, string secret)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/session");
        request.Headers.Add("Cookie", BrowserCookie.PlainName + "=" + secret);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    private static async Task<string?> Code(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemResponse>(
            TestContext.Current.CancellationToken))?.Code;

    private static string CookieValue(string setCookie) =>
        setCookie.Split(';', 2)[0].Split('=', 2)[1];

}
