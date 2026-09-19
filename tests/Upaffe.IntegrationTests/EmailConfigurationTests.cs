using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Upaffe.Api.Http;
using Upaffe.Application.Access;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class EmailConfigurationTests(PostgresFixture postgres)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Fact]
    public async Task Settings_and_recipients_are_versioned_and_password_is_write_only()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var instance = AnInstance.Against(await postgres.CreateDatabaseAsync(),
            new Dictionary<string, string?>
            { [BootstrapSettings.Variable] = "a-valid-bootstrap-proof-for-email-tests" });
        using var client = instance.CreateClient();
        using var bootstrap = await client.PostAsJsonAsync("/api/bootstrap",
            new BootstrapRequest("a-valid-bootstrap-proof-for-email-tests",
                "operator@example.test", "a long operator password"), Json, ct);
        Assert.Equal(HttpStatusCode.NoContent, bootstrap.StatusCode);
        using var signIn = await client.PostAsJsonAsync("/api/session",
            new SignInRequest("operator@example.test", "a long operator password"), Json, ct);
        var cookie = Assert.Single(signIn.Headers.GetValues("Set-Cookie")).Split(';')[0];
        using var credentialRequest = new HttpRequestMessage(HttpMethod.Post, "/api/management-credentials")
        { Content = JsonContent.Create(new CreateCredentialRequest("email test agent")) };
        credentialRequest.Headers.Add("Cookie", cookie);
        credentialRequest.Headers.Add(CsrfProtection.Header, "1");
        credentialRequest.Headers.Add("Origin", "http://localhost");
        using var credentialResponse = await client.SendAsync(credentialRequest, ct);
        var issued = await credentialResponse.Content.ReadFromJsonAsync<IssuedCredentialResponse>(Json, ct);
        Assert.NotNull(issued);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued.Token);

        using var read = await client.GetAsync("/api/email/settings", ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var initial = JsonDocument.Parse(await read.Content.ReadAsStringAsync(ct));
        Assert.Equal(0, initial.RootElement.GetProperty("version").GetInt64());
        Assert.False(initial.RootElement.GetProperty("has_password").GetBoolean());

        using var updated = await client.PutAsJsonAsync("/api/email/settings",
            new UpdateEmailSettingsRequest(0, "mail.example.test", 587, "starttls",
                "notify@example.test", "Monitor", "https://status.example.test",
                "smtp-user"), Json, ct);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var settings = JsonDocument.Parse(await updated.Content.ReadAsStringAsync(ct));
        var version = settings.RootElement.GetProperty("version").GetInt64();

        using var password = await client.PutAsJsonAsync("/api/email/password",
            new SetSmtpPasswordRequest(version, "private-test-password"), Json, ct);
        Assert.Equal(HttpStatusCode.OK, password.StatusCode);
        var passwordText = await password.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("private-test-password", passwordText);
        Assert.Contains("\"has_password\":true", passwordText);
        version++;

        using var defaults = await client.PutAsJsonAsync("/api/email/default-recipients",
            new ReplaceRecipientsRequest(version, [" Alerts@Example.TEST "]), Json, ct);
        Assert.Equal(HttpStatusCode.OK, defaults.StatusCode);
        version++;
        using var first = await client.PostAsJsonAsync("/api/projects",
            new CreateProjectRequest("first-project", "First"), Json, ct);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var firstRecipients = await client.GetAsync("/api/projects/first-project/recipients", ct);
        var firstData = JsonDocument.Parse(await firstRecipients.Content.ReadAsStringAsync(ct));
        Assert.Equal("Alerts@example.test", firstData.RootElement.GetProperty("recipients")[0].GetString());

        using var changeDefaults = await client.PutAsJsonAsync("/api/email/default-recipients",
            new ReplaceRecipientsRequest(version, ["new@example.test"]), Json, ct);
        Assert.Equal(HttpStatusCode.OK, changeDefaults.StatusCode);
        using var second = await client.PostAsJsonAsync("/api/projects",
            new CreateProjectRequest("second-project", "Second"), Json, ct);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        using var stillFirst = await client.GetAsync("/api/projects/first-project/recipients", ct);
        Assert.Contains("Alerts@example.test", await stillFirst.Content.ReadAsStringAsync(ct));
        using var secondRecipients = await client.GetAsync("/api/projects/second-project/recipients", ct);
        Assert.Contains("new@example.test", await secondRecipients.Content.ReadAsStringAsync(ct));

        using var duplicate = await client.PutAsJsonAsync("/api/projects/first-project/recipients",
            new ReplaceRecipientsRequest(1, ["A@example.test", "a@EXAMPLE.test"]), Json, ct);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using var replaced = await client.PutAsJsonAsync("/api/projects/first-project/recipients",
            new ReplaceRecipientsRequest(1, ["ops@example.test"]), Json, ct);
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        using var stale = await client.PutAsJsonAsync("/api/projects/first-project/recipients",
            new ReplaceRecipientsRequest(1, ["stale@example.test"]), Json, ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var anonymous = instance.CreateClient(new WebApplicationFactoryClientOptions
        { HandleCookies = false });
        using var unauthorized = await anonymous.GetAsync("/api/email/settings", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }
}
