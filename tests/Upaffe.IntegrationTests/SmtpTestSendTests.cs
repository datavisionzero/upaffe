using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Upaffe.Api.Http;
using Upaffe.Application.Access;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class SmtpTestSendTests(PostgresFixture postgres)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Fact]
    public async Task Test_send_reports_smtp_acceptance_and_sanitized_failure()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var instance = AnInstance.Against(await postgres.CreateDatabaseAsync());
        using var client = instance.CreateClient(new WebApplicationFactoryClientOptions
        { HandleCookies = false });
        await instance.EstablishAsync("operator@example.test", "a long operator password");
        using var signin = await client.PostAsJsonAsync("/api/session",
            new SignInRequest("operator@example.test", "a long operator password"), Json, ct);
        var cookie = Assert.Single(signin.Headers.GetValues("Set-Cookie")).Split(';')[0];
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/management-credentials")
        { Content = JsonContent.Create(new CreateCredentialRequest("smtp test agent"), options: Json) };
        create.Headers.Add("Cookie", cookie);
        create.Headers.Add(CsrfProtection.Header, "1");
        create.Headers.Add("Origin", "http://localhost");
        using var created = await client.SendAsync(create, ct);
        var credential = await created.Content.ReadFromJsonAsync<IssuedCredentialResponse>(Json, ct);
        Assert.NotNull(credential);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);

        using var unconfigured = await client.PostAsJsonAsync("/api/email/test",
            new TestEmailRequest("recipient@example.test"), Json, ct);
        Assert.Equal(HttpStatusCode.Conflict, unconfigured.StatusCode);
        Assert.Contains("email_not_configured", await unconfigured.Content.ReadAsStringAsync(ct));

        await using var relay = new FakeSmtpServer(false, ct);
        using var configured = await client.PutAsJsonAsync("/api/email/settings",
            new UpdateEmailSettingsRequest(0, "127.0.0.1", relay.Port, "none",
                "notify@example.test", "upaffe", "https://status.example.test", null), Json, ct);
        Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
        using var accepted = await client.PostAsJsonAsync("/api/email/test",
            new TestEmailRequest("recipient@example.test"), Json, ct);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var response = await accepted.Content.ReadAsStringAsync(ct);
        Assert.Contains("accepted_by_smtp", response);
        Assert.DoesNotContain("notify@example.test", response);
        Assert.DoesNotContain("recipient@example.test", response);
        var message = await relay.Message;
        Assert.Contains("Subject: upaffe SMTP test", message);
        Assert.Contains("recipient@example.test", message);

        await using var rejecting = new FakeSmtpServer(true, ct);
        using var rejectedConfig = await client.PutAsJsonAsync("/api/email/settings",
            new UpdateEmailSettingsRequest(1, "127.0.0.1", rejecting.Port, "none",
                "notify@example.test", "upaffe", "https://status.example.test", null), Json, ct);
        Assert.Equal(HttpStatusCode.OK, rejectedConfig.StatusCode);
        using var rejected = await client.PostAsJsonAsync("/api/email/test",
            new TestEmailRequest("recipient@example.test"), Json, ct);
        Assert.Equal(HttpStatusCode.BadGateway, rejected.StatusCode);
        var failure = await rejected.Content.ReadAsStringAsync(ct);
        Assert.Contains("smtp_rejected", failure);
        Assert.DoesNotContain("private relay diagnostic", failure);

        using var anonymous = instance.CreateClient(new WebApplicationFactoryClientOptions
        { HandleCookies = false });
        using var forbidden = await anonymous.PostAsJsonAsync("/api/email/test",
            new TestEmailRequest("recipient@example.test"), Json, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, forbidden.StatusCode);
    }

    private sealed class FakeSmtpServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop;
        public int Port { get; }
        public Task<string> Message { get; }

        public FakeSmtpServer(bool reject, CancellationToken cancellationToken)
        {
            stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Message = ServeAsync(reject, stop.Token);
        }

        private async Task<string> ServeAsync(bool reject, CancellationToken ct)
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
            { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 fake relay".AsMemory(), ct);
            var data = new StringBuilder();
            var inData = false;
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (inData)
                {
                    if (line == ".") { inData = false; await writer.WriteLineAsync("250 queued".AsMemory(), ct); }
                    else data.AppendLine(line);
                }
                else if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250-fake relay".AsMemory(), ct);
                    await writer.WriteLineAsync("250 SIZE 1048576".AsMemory(), ct);
                }
                else if (line.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase) && reject)
                    await writer.WriteLineAsync("550 private relay diagnostic".AsMemory(), ct);
                else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                { inData = true; await writer.WriteLineAsync("354 send data".AsMemory(), ct); }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                { await writer.WriteLineAsync("221 bye".AsMemory(), ct); break; }
                else await writer.WriteLineAsync("250 okay".AsMemory(), ct);
            }
            return data.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            stop.Cancel();
            listener.Stop();
            try { await Message; } catch (OperationCanceledException) { }
            stop.Dispose();
        }
    }
}
