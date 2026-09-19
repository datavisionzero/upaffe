using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;
using Upaffe.Infrastructure.Monitoring;

namespace Upaffe.UnitTests;

public sealed class HttpCheckExecutorTests
{
    private static readonly IPAddress PublicOne = IPAddress.Parse("192.0.0.9");
    private static readonly IPAddress PublicTwo = IPAddress.Parse("192.0.0.10");

    [Fact]
    public async Task A_successful_check_is_bounded_structured_and_redacted()
    {
        await using var server = new LoopbackHttpServer(
            LoopbackResponse.Http(200, "ready for traffic", "text/plain; charset=utf-8"));
        var executor = Executor(server, Resolver(("status.example", [PublicOne])));

        var result = await executor.ExecuteAsync(
            Request(
                "http://status.example/health?credential=never-returned",
                headers: [new("Authorization", "Bearer never-returned")],
                textCondition: TextCondition.Required,
                text: "ready"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Null(result.ReasonCode);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("http://status.example/health", result.EffectiveUrl);
        Assert.DoesNotContain("never-returned", result.Message, StringComparison.Ordinal);
        var sent = Assert.Single(server.Requests);
        Assert.Contains("Authorization: Bearer never-returned", sent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Status_and_text_failures_have_stable_reasons_without_response_content()
    {
        await using var statusServer = new LoopbackHttpServer(LoopbackResponse.Http(503, "private diagnostic"));
        var status = await Executor(statusServer).ExecuteAsync(
            Request("http://status.example/health"),
            TestContext.Current.CancellationToken);
        Assert.Equal("unexpected_status", status.ReasonCode);
        Assert.DoesNotContain("private diagnostic", status.Message, StringComparison.Ordinal);

        await using var missingServer = new LoopbackHttpServer(LoopbackResponse.Http(200, "starting"));
        var missing = await Executor(missingServer).ExecuteAsync(
            Request(
                "http://status.example/health",
                textCondition: TextCondition.Required,
                text: "ready"),
            TestContext.Current.CancellationToken);
        Assert.Equal("required_text_missing", missing.ReasonCode);

        await using var forbiddenServer = new LoopbackHttpServer(LoopbackResponse.Http(200, "maintenance"));
        var forbidden = await Executor(forbiddenServer).ExecuteAsync(
            Request(
                "http://status.example/health",
                textCondition: TextCondition.Forbidden,
                text: "maintenance"),
            TestContext.Current.CancellationToken);
        Assert.Equal("forbidden_text_present", forbidden.ReasonCode);
    }

    [Fact]
    public async Task Redirects_revalidate_DNS_and_strip_headers_when_the_origin_changes()
    {
        await using var server = new LoopbackHttpServer(
            LoopbackResponse.Redirect("http://other.example/final"),
            LoopbackResponse.Http(200, "ready"));
        var resolver = Resolver(
            ("status.example", [PublicOne]),
            ("other.example", [PublicTwo]));
        var result = await Executor(server, resolver).ExecuteAsync(
            Request(
                "http://status.example/start",
                headers: [new("Authorization", "Bearer write-only")]),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(2, server.Requests.Count);
        Assert.Contains("Authorization: Bearer write-only", server.Requests[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", server.Requests[1], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["status.example", "other.example"], resolver.RequestedHosts);
    }

    [Fact]
    public async Task A_changed_DNS_answer_is_rejected_on_the_next_redirect_hop()
    {
        await using var server = new LoopbackHttpServer(LoopbackResponse.Redirect("/again"));
        var resolver = new SequenceResolver(
            ("status.example", [PublicOne]),
            ("status.example", [IPAddress.Loopback]));

        var result = await Executor(server, resolver).ExecuteAsync(
            Request("http://status.example/start"),
            TestContext.Current.CancellationToken);

        Assert.Equal("target_not_allowed", result.ReasonCode);
        Assert.Single(server.Requests);
        Assert.Equal(["status.example", "status.example"], resolver.RequestedHosts);
    }

    [Fact]
    public async Task A_port_change_strips_secret_headers_even_on_the_same_host()
    {
        await using var server = new LoopbackHttpServer(
            LoopbackResponse.Redirect("http://status.example:8080/final"),
            LoopbackResponse.Http(200, "ready"));
        var resolver = Resolver(
            ("status.example", [PublicOne]),
            ("status.example", [PublicOne]));
        var result = await Executor(server, resolver).ExecuteAsync(
            Request("http://status.example/start", headers: [new("Authorization", "Bearer write-only")]),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Contains("Authorization: Bearer write-only", server.Requests[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", server.Requests[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_sixth_redirect_fails_without_opening_another_connection()
    {
        await using var server = new LoopbackHttpServer(
            Enumerable.Repeat(LoopbackResponse.Redirect("/again"), 6).ToArray());
        var resolver = Resolver(Enumerable.Repeat(("status.example", new[] { PublicOne }), 6).ToArray());
        var result = await Executor(server, resolver).ExecuteAsync(
            Request("http://status.example/start"), TestContext.Current.CancellationToken);

        Assert.Equal("too_many_redirects", result.ReasonCode);
        Assert.Equal(6, server.Requests.Count);
    }

    [Fact]
    public async Task Private_or_mixed_DNS_answers_never_reach_the_connector()
    {
        var connector = new RejectingConnector();
        var privateResult = await new HttpCheckExecutor(
            Resolver(("private.example", [IPAddress.Parse("10.0.0.8")])),
            connector).ExecuteAsync(
                Request("http://private.example/health"),
                TestContext.Current.CancellationToken);
        Assert.Equal("target_not_allowed", privateResult.ReasonCode);

        var mixedResult = await new HttpCheckExecutor(
            Resolver(("mixed.example", [PublicOne, IPAddress.Loopback])),
            connector).ExecuteAsync(
                Request("http://mixed.example/health"),
                TestContext.Current.CancellationToken);
        Assert.Equal("target_not_allowed", mixedResult.ReasonCode);
        Assert.Equal(0, connector.Calls);
    }

    [Fact]
    public async Task Private_IP_literals_bypass_neither_DNS_nor_address_policy()
    {
        var resolver = Resolver();
        var connector = new RejectingConnector();
        var result = await new HttpCheckExecutor(resolver, connector).ExecuteAsync(
            Request("http://127.0.0.1/health"),
            TestContext.Current.CancellationToken);

        Assert.Equal("target_not_allowed", result.ReasonCode);
        Assert.Empty(resolver.RequestedHosts);
        Assert.Equal(0, connector.Calls);
    }

    [Theory]
    [InlineData("http://[::ffff:127.0.0.1]/health")]
    [InlineData("http://[::1]/health")]
    [InlineData("http://[2001:db8::1]/health")]
    [InlineData("http://169.254.169.254/health")]
    public async Task Forbidden_IPv4_and_IPv6_literals_never_connect(string target)
    {
        var connector = new RejectingConnector();
        var result = await new HttpCheckExecutor(Resolver(), connector).ExecuteAsync(
            Request(target), TestContext.Current.CancellationToken);

        Assert.Equal("target_not_allowed", result.ReasonCode);
        Assert.Equal(0, connector.Calls);
    }

    [Fact]
    public async Task A_redirect_to_a_forbidden_literal_never_connects_again()
    {
        await using var server = new LoopbackHttpServer(
            LoopbackResponse.Redirect("http://[::ffff:127.0.0.1]/private"));
        var result = await Executor(server).ExecuteAsync(
            Request("http://status.example/start"), TestContext.Current.CancellationToken);

        Assert.Equal("target_not_allowed", result.ReasonCode);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task A_pinned_socket_uses_only_the_supplied_address()
    {
        await using var server = new LoopbackHttpServer(LoopbackResponse.Http(200, "ready"));
        var factory = new SocketPinnedConnectionFactory();
        await using var stream = await factory.ConnectAsync(
            [IPAddress.Loopback], server.Port, TestContext.Current.CancellationToken);
        await stream.WriteAsync(
            "GET / HTTP/1.1\r\nHost: status.example\r\nConnection: close\r\n\r\n"u8.ToArray(),
            TestContext.Current.CancellationToken);
        var response = new byte[512];
        var read = await stream.ReadAsync(response, TestContext.Current.CancellationToken);

        Assert.Contains("HTTP/1.1 200", Encoding.ASCII.GetString(response, 0, read));
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task Timeout_and_caller_cancellation_stop_pending_IO()
    {
        await using var timeoutServer = new LoopbackHttpServer(
            new LoopbackResponse([], TimeSpan.FromSeconds(5), ReadRequest: true));
        var watch = Stopwatch.StartNew();
        var timedOut = await Executor(timeoutServer).ExecuteAsync(
            Request("http://status.example/slow", timeoutSeconds: 1),
            TestContext.Current.CancellationToken);
        Assert.Equal("timeout", timedOut.ReasonCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));

        await using var cancellationServer = new LoopbackHttpServer(
            new LoopbackResponse([], TimeSpan.FromSeconds(5), ReadRequest: true));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        watch.Restart();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Executor(cancellationServer).ExecuteAsync(
                Request("http://status.example/cancel", timeoutSeconds: 10),
                cancellation.Token));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task TLS_failures_are_distinct_from_connection_failures()
    {
        await using var server = new LoopbackHttpServer(
            new LoopbackResponse(
                Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"),
                TimeSpan.Zero,
                ReadRequest: false));
        var result = await Executor(server).ExecuteAsync(
            Request("https://status.example/health"),
            TestContext.Current.CancellationToken);

        Assert.Equal("tls_failed", result.ReasonCode);
    }

    [Fact]
    public async Task The_decoded_body_limit_cannot_be_bypassed_with_compression()
    {
        var decoded = new byte[(1_024 * 1_024) + 1];
        Array.Fill(decoded, (byte)'a');
        byte[] compressed;
        using (var output = new MemoryStream())
        {
            using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                await gzip.WriteAsync(decoded, TestContext.Current.CancellationToken);
            }

            compressed = output.ToArray();
        }

        await using var server = new LoopbackHttpServer(
            LoopbackResponse.Http(200, compressed, "application/octet-stream", "gzip"));
        var result = await Executor(server).ExecuteAsync(
            Request("http://status.example/large"),
            TestContext.Current.CancellationToken);

        Assert.Equal("response_too_large", result.ReasonCode);
    }

    [Fact]
    public async Task A_streamed_body_cannot_cross_the_decoded_limit()
    {
        await using var server = new LoopbackHttpServer(LoopbackResponse.Http(
            200, new byte[1_024 * 1_024 + 1], "application/octet-stream"));
        var result = await Executor(server).ExecuteAsync(
            Request("http://status.example/stream"), TestContext.Current.CancellationToken);

        Assert.Equal("response_too_large", result.ReasonCode);
    }

    [Fact]
    public async Task Oversized_response_headers_fail_before_body_processing()
    {
        var oversized = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n"
            + "X-Oversized: "
            + new string('a', 65 * 1_024)
            + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await using var server = new LoopbackHttpServer(
            new LoopbackResponse(oversized, TimeSpan.Zero, ReadRequest: true));
        var result = await Executor(server).ExecuteAsync(
            Request("http://status.example/headers"),
            TestContext.Current.CancellationToken);

        Assert.Equal("response_headers_too_large", result.ReasonCode);
    }

    [Fact]
    public async Task Invalid_declared_or_actual_text_encoding_is_a_stable_failure()
    {
        await using var server = new LoopbackHttpServer(
            LoopbackResponse.Http(200, [0xff, 0xfe, 0xfd], "text/plain; charset=utf-8"));
        var result = await Executor(server).ExecuteAsync(
            Request(
                "http://status.example/encoding",
                textCondition: TextCondition.Required,
                text: "ready"),
            TestContext.Current.CancellationToken);

        Assert.Equal("response_encoding_invalid", result.ReasonCode);
    }

    [Theory]
    [InlineData("192.0.0.10", true)]
    [InlineData("192.0.0.9", true)]
    [InlineData("10.0.0.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("0.0.0.1", false)]
    [InlineData("192.0.2.1", false)]
    [InlineData("192.0.0.8", false)]
    [InlineData("192.88.99.2", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("203.0.113.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("2001:4860:4860::8888", true)]
    [InlineData("2001:1::1", true)]
    [InlineData("::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("2001:2::1", false)]
    [InlineData("2002::1", false)]
    [InlineData("3fff::1", false)]
    [InlineData("5f00::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("64:ff9b::0808:0808", true)]
    [InlineData("64:ff9b::0a00:0001", false)]
    public void Only_IANA_globally_reachable_addresses_are_allowed(string value, bool allowed) =>
        Assert.Equal(allowed, PublicInternetAddress.IsAllowed(IPAddress.Parse(value)));

    private static HttpCheckExecutor Executor(
        LoopbackHttpServer server,
        SequenceResolver? resolver = null) =>
        new(resolver ?? Resolver(("status.example", [PublicOne])), new LoopbackConnector(server.Port));

    private static SequenceResolver Resolver(params (string Host, IPAddress[] Addresses)[] answers) =>
        new(answers);

    private static HttpExecutionRequest Request(
        string target,
        IReadOnlyList<HttpExecutionHeader>? headers = null,
        TextCondition textCondition = TextCondition.None,
        string? text = null,
        int timeoutSeconds = 5) =>
        new(target, headers ?? [], 200, textCondition, text, timeoutSeconds);

    private sealed class SequenceResolver(params (string Host, IPAddress[] Addresses)[] answers) : IHostResolver
    {
        private readonly Queue<(string Host, IPAddress[] Addresses)> answers = new(answers);

        public List<string> RequestedHosts { get; } = [];

        public Task<IReadOnlyList<IPAddress>> ResolveAsync(
            string host,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedHosts.Add(host);
            if (answers.Count == 0)
            {
                throw new SocketException((int)SocketError.HostNotFound);
            }

            var answer = answers.Dequeue();
            Assert.Equal(answer.Host, host);
            return Task.FromResult<IReadOnlyList<IPAddress>>(answer.Addresses);
        }
    }

    private sealed class LoopbackConnector(int serverPort) : IPinnedConnectionFactory
    {
        public async ValueTask<Stream> ConnectAsync(
            IReadOnlyList<IPAddress> addresses,
            int port,
            CancellationToken cancellationToken)
        {
            Assert.All(addresses, address => Assert.True(PublicInternetAddress.IsAllowed(address)));
            Assert.InRange(port, 1, 65_535);
            var client = new TcpClient();
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, serverPort, cancellationToken);
                return client.GetStream();
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
    }

    private sealed class RejectingConnector : IPinnedConnectionFactory
    {
        public int Calls { get; private set; }

        public ValueTask<Stream> ConnectAsync(
            IReadOnlyList<IPAddress> addresses,
            int port,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("A forbidden address reached the connector.");
        }
    }

    private sealed record LoopbackResponse(byte[] Bytes, TimeSpan Delay, bool ReadRequest)
    {
        public static LoopbackResponse Redirect(string location) =>
            Http(302, string.Empty, extraHeaders: $"Location: {location}\r\n");

        public static LoopbackResponse Http(
            int status,
            string body,
            string contentType = "text/plain; charset=utf-8",
            string extraHeaders = "") =>
            Http(status, Encoding.UTF8.GetBytes(body), contentType, null, extraHeaders);

        public static LoopbackResponse Http(
            int status,
            byte[] body,
            string contentType,
            string? contentEncoding = null,
            string extraHeaders = "")
        {
            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status} Result\r\n"
                + $"Content-Length: {body.Length}\r\n"
                + $"Content-Type: {contentType}\r\n"
                + (contentEncoding is null ? string.Empty : $"Content-Encoding: {contentEncoding}\r\n")
                + extraHeaders
                + "Connection: close\r\n\r\n");
            return new([.. headers, .. body], TimeSpan.Zero, ReadRequest: true);
        }
    }

    private sealed class LoopbackHttpServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task serving;

        public LoopbackHttpServer(params LoopbackResponse[] responses)
        {
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            serving = ServeAsync(responses, cancellation.Token);
        }

        public int Port { get; }
        public List<string> Requests { get; } = [];

        public async ValueTask DisposeAsync()
        {
            await cancellation.CancelAsync();
            listener.Stop();
            try
            {
                await serving;
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException)
            {
            }

            cancellation.Dispose();
        }

        private async Task ServeAsync(LoopbackResponse[] responses, CancellationToken cancellationToken)
        {
            foreach (var response in responses)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                await using var stream = client.GetStream();
                if (response.ReadRequest)
                {
                    Requests.Add(await ReadHeadersAsync(stream, cancellationToken));
                }

                if (response.Delay > TimeSpan.Zero)
                {
                    await Task.Delay(response.Delay, cancellationToken);
                }

                if (response.Bytes.Length > 0)
                {
                    await stream.WriteAsync(response.Bytes, cancellationToken);
                }
            }
        }

        private static async Task<string> ReadHeadersAsync(Stream stream, CancellationToken cancellationToken)
        {
            using var bytes = new MemoryStream();
            var single = new byte[1];
            while (bytes.Length < 64 * 1_024)
            {
                var read = await stream.ReadAsync(single, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                bytes.WriteByte(single[0]);
                var span = bytes.GetBuffer().AsSpan(0, (int)bytes.Length);
                if (span.EndsWith("\r\n\r\n"u8))
                {
                    return Encoding.ASCII.GetString(span);
                }
            }

            throw new IOException("The test request headers were incomplete.");
        }
    }
}
