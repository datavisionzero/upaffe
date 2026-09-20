using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Upaffe.Api.Hosting;
using Upaffe.Api.Http;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class ReverseProxyTests(PostgresFixture postgres)
{
    private const string Email = "operator@example.test";
    private const string Password = "a long operator password";
    private const string PublicOrigin = "https://status.example.test";
    private static readonly IPAddress Proxy = IPAddress.Parse("192.0.2.10");
    private static readonly IPAddress DirectClient = IPAddress.Parse("198.51.100.20");

    [Theory]
    [InlineData("192.0.2.10", null)]
    [InlineData(null, PublicOrigin)]
    [InlineData("0.0.0.0", PublicOrigin)]
    [InlineData("192.0.2.10/24", PublicOrigin)]
    [InlineData("192.0.2.10", "http://status.example.test")]
    [InlineData("192.0.2.10", "https://status.example.test/path")]
    public void Unsafe_or_incomplete_proxy_configuration_is_rejected(
        string? proxyAddresses, string? publicOrigin)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [TrustedProxySettings.ProxyAddressesVariable] = proxyAddresses,
                [TrustedProxySettings.PublicOriginVariable] = publicOrigin,
            }).Build();

        Assert.Throws<InvalidOperationException>(() => TrustedProxySettings.Read(configuration));
    }

    [Fact]
    public async Task Trusted_https_proxy_issues_secure_cookie_and_accepts_same_origin_write()
    {
        await using var instance = await EstablishedAsync(Proxy);
        using var client = Client(instance);
        using var signIn = SignIn("203.0.113.7");
        using var signedIn = await client.SendAsync(signIn, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, signedIn.StatusCode);
        var cookie = Assert.Single(signedIn.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith(BrowserCookie.SecureName + "=", cookie, StringComparison.Ordinal);
        Assert.Contains("; secure", cookie, StringComparison.OrdinalIgnoreCase);

        using var create = CreateProject("proxy-project", BrowserCookie.SecureName,
            CookieValue(cookie), PublicOrigin, "203.0.113.7");
        using var created = await client.SendAsync(create, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var wrongOrigin = CreateProject("wrong-origin", BrowserCookie.SecureName,
            CookieValue(cookie), "https://elsewhere.example.test", "203.0.113.7");
        using var refused = await client.SendAsync(wrongOrigin, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        using var wrongScheme = CreateProject("wrong-scheme", BrowserCookie.SecureName,
            CookieValue(cookie), "http://status.example.test", "203.0.113.7");
        using var schemeRefused = await client.SendAsync(wrongScheme, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, schemeRefused.StatusCode);

        using var wrongHost = CreateProject("wrong-host", BrowserCookie.SecureName,
            CookieValue(cookie), PublicOrigin, "203.0.113.7");
        wrongHost.Headers.Remove("X-Forwarded-Host");
        wrongHost.Headers.Add("X-Forwarded-Host", "elsewhere.example.test");
        using var hostRefused = await client.SendAsync(wrongHost, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, hostRefused.StatusCode);
    }

    [Fact]
    public async Task Untrusted_forwarded_headers_do_not_change_cookie_or_csrf_origin()
    {
        await using var instance = await EstablishedAsync(DirectClient);
        using var client = Client(instance);
        using var signIn = SignIn("203.0.113.7");
        using var signedIn = await client.SendAsync(signIn, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, signedIn.StatusCode);
        var cookie = Assert.Single(signedIn.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith(BrowserCookie.PlainName + "=", cookie, StringComparison.Ordinal);
        Assert.DoesNotContain("; secure", cookie, StringComparison.OrdinalIgnoreCase);

        using var spoofed = CreateProject("spoofed-project", BrowserCookie.PlainName,
            CookieValue(cookie), PublicOrigin, "203.0.113.7");
        using var refused = await client.SendAsync(spoofed, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        using var local = CreateProject("local-project", BrowserCookie.PlainName,
            CookieValue(cookie), "http://localhost", "203.0.113.7");
        using var created = await client.SendAsync(local, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Theory]
    [InlineData(true, HttpStatusCode.NoContent)]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    public async Task Login_throttle_uses_the_real_client_only_for_trusted_proxy(
        bool trusted, HttpStatusCode expected)
    {
        await using var instance = await EstablishedAsync(trusted ? Proxy : DirectClient);
        using var client = Client(instance);

        for (var attempt = 0; attempt < LoginThrottle.AddressLimit; attempt++)
        {
            using var failed = SignIn($"203.0.113.{attempt + 1}",
                $"other-{attempt}@example.test", "wrong password");
            using var response = await client.SendAsync(failed, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var valid = SignIn("203.0.113.200");
        using var accepted = await client.SendAsync(valid, TestContext.Current.CancellationToken);
        Assert.Equal(expected, accepted.StatusCode);
    }

    private async Task<AnInstance> EstablishedAsync(IPAddress remoteAddress)
    {
        var connection = await postgres.CreateDatabaseAsync();
        var settings = new Dictionary<string, string?>
        {
            [TrustedProxySettings.ProxyAddressesVariable] = Proxy.ToString(),
            [TrustedProxySettings.PublicOriginVariable] = PublicOrigin,
        };
        var instance = AnInstance.Against(connection, settings, remoteAddress: remoteAddress);
        using var client = Client(instance);
        await instance.EstablishAsync(Email, Password);
        return instance;
    }

    private static HttpClient Client(AnInstance instance) =>
        instance.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            BaseAddress = new Uri("http://localhost"),
        });

    private static HttpRequestMessage SignIn(
        string clientAddress, string email = Email, string password = Password)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/session")
        {
            Content = JsonContent.Create(new SignInRequest(email, password)),
        };
        Forward(request, clientAddress);
        return request;
    }

    private static HttpRequestMessage CreateProject(
        string key, string cookieName, string cookie, string origin, string clientAddress)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/projects")
        {
            Content = JsonContent.Create(new { key, name = "Proxy test project" }),
        };
        Forward(request, clientAddress);
        request.Headers.Add("Cookie", cookieName + "=" + cookie);
        request.Headers.Add(CsrfProtection.Header, "1");
        request.Headers.Add("Origin", origin);
        return request;
    }

    private static void Forward(HttpRequestMessage request, string clientAddress)
    {
        request.Headers.Add("X-Forwarded-For", clientAddress);
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "status.example.test");
    }

    private static string CookieValue(string setCookie) =>
        setCookie.Split(';', 2)[0].Split('=', 2)[1];
}
