using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;

namespace Upaffe.Infrastructure.Monitoring;

public sealed class HttpCheckExecutor(
    IHostResolver resolver,
    IPinnedConnectionFactory connections) : IHttpCheckExecutor
{
    private const int MaximumRedirects = 5;
    private const int MaximumBodyBytes = 1_024 * 1_024;
    private const int MaximumHeaderBytes = 16 * 1_024;

    public async Task<HttpExecutionResult> ExecuteAsync(
        HttpExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var started = Stopwatch.GetTimestamp();
        Uri initial;
        try
        {
            initial = AcceptedTarget(request.TargetUrl);
            ValidateRequest(request);
        }
        catch (ArgumentException)
        {
            return Failure("target_invalid", "The HTTP target or check configuration is invalid.", started);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var current = initial;
            for (var redirects = 0; ; redirects++)
            {
                var addresses = await ResolveAndAuthorizeAsync(current, bounded.Token);
                using var handler = Handler(addresses);
                using var client = new HttpClient(handler)
                {
                    Timeout = Timeout.InfiniteTimeSpan,
                };
                using var message = new HttpRequestMessage(HttpMethod.Get, current);
                message.Headers.UserAgent.ParseAdd("upaffe-monitor/1");
                if (SameOrigin(initial, current))
                {
                    foreach (var header in request.Headers)
                    {
                        message.Headers.TryAddWithoutValidation(header.Name, header.Value);
                    }
                }

                using var response = await client.SendAsync(
                    message,
                    HttpCompletionOption.ResponseHeadersRead,
                    bounded.Token);
                if (IsRedirect(response.StatusCode) && response.Headers.Location is not null)
                {
                    if (redirects >= MaximumRedirects)
                    {
                        return Failure(
                            "too_many_redirects",
                            "The HTTP response exceeded the redirect limit.",
                            started,
                            (int)response.StatusCode,
                            current);
                    }

                    Uri next;
                    try
                    {
                        next = AcceptedTarget(new Uri(current, response.Headers.Location).AbsoluteUri);
                    }
                    catch (ArgumentException)
                    {
                        return Failure(
                            "redirect_invalid",
                            "The HTTP response contained an invalid redirect target.",
                            started,
                            (int)response.StatusCode,
                            current);
                    }

                    if (current.Scheme == Uri.UriSchemeHttps && next.Scheme == Uri.UriSchemeHttp)
                    {
                        return Failure(
                            "redirect_downgrade",
                            "An HTTPS check cannot redirect to HTTP.",
                            started,
                            (int)response.StatusCode,
                            current);
                    }

                    current = next;
                    continue;
                }

                var body = await ReadBodyAsync(response, bounded.Token);
                var statusCode = (int)response.StatusCode;
                if (statusCode != request.ExpectedStatusCode)
                {
                    return Failure(
                        "unexpected_status",
                        $"Expected HTTP {request.ExpectedStatusCode}, received HTTP {statusCode}.",
                        started,
                        statusCode,
                        current);
                }

                if (request.TextCondition != TextCondition.None)
                {
                    var decoded = Decode(body, response.Content.Headers.ContentType?.CharSet);
                    var contains = decoded.Contains(request.TextFragment!, StringComparison.Ordinal);
                    if (request.TextCondition == TextCondition.Required && !contains)
                    {
                        return Failure(
                            "required_text_missing",
                            "The required text was not present in the response.",
                            started,
                            statusCode,
                            current);
                    }

                    if (request.TextCondition == TextCondition.Forbidden && contains)
                    {
                        return Failure(
                            "forbidden_text_present",
                            "The forbidden text was present in the response.",
                            started,
                            statusCode,
                            current);
                    }
                }

                return new(
                    true,
                    null,
                    "The HTTP check succeeded.",
                    statusCode,
                    ElapsedMilliseconds(started),
                    Redacted(current));
            }
        }
        catch (TargetNotAllowedException)
        {
            return Failure("target_not_allowed", "The HTTP target is not publicly routable.", started);
        }
        catch (HostResolutionException)
        {
            return Failure("dns_failed", "The HTTP target could not be resolved.", started);
        }
        catch (ResponseTooLargeException)
        {
            return Failure("response_too_large", "The HTTP response exceeded the size limit.", started);
        }
        catch (ResponseEncodingException)
        {
            return Failure("response_encoding_invalid", "The HTTP response text encoding is invalid.", started);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return Failure("timeout", "The HTTP check exceeded its timeout.", started);
        }
        catch (HttpRequestException exception) when (
            exception.HttpRequestError == HttpRequestError.ConfigurationLimitExceeded)
        {
            return Failure("response_headers_too_large", "The HTTP response headers exceeded the size limit.", started);
        }
        catch (HttpRequestException exception) when (
            exception.HttpRequestError == HttpRequestError.SecureConnectionError
            || HasTlsFailure(exception))
        {
            return Failure("tls_failed", "The HTTPS connection failed certificate or protocol validation.", started);
        }
        catch (HttpRequestException)
        {
            return Failure("connection_failed", "The HTTP connection failed.", started);
        }
        catch (IOException)
        {
            return Failure("connection_failed", "The HTTP connection failed.", started);
        }
    }

    private SocketsHttpHandler Handler(IReadOnlyList<IPAddress> addresses) => new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        ConnectCallback = (context, cancellationToken) =>
            connections.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken),
        MaxResponseHeadersLength = 64,
        PooledConnectionLifetime = TimeSpan.Zero,
        PreAuthenticate = false,
        UseCookies = false,
        UseProxy = false,
    };

    private async Task<IReadOnlyList<IPAddress>> ResolveAndAuthorizeAsync(
        Uri target,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IPAddress> addresses;
        if (target.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            addresses = [IPAddress.Parse(target.DnsSafeHost)];
        }
        else
        {
            try
            {
                addresses = await resolver.ResolveAsync(target.IdnHost, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is SocketException or ArgumentException)
            {
                throw new HostResolutionException(exception);
            }
        }

        if (addresses.Count == 0)
        {
            throw new HostResolutionException();
        }

        if (addresses.Any(address => !PublicInternetAddress.IsAllowed(address)))
        {
            throw new TargetNotAllowedException();
        }

        return addresses.Distinct().ToArray();
    }

    private static Uri AcceptedTarget(string value)
    {
        var target = (value ?? string.Empty).Trim();
        if (target.Length is < 1 or > HttpMonitor.MaximumTargetLength
            || !Uri.TryCreate(target, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.Port == 0)
        {
            throw new ArgumentException("The HTTP target is invalid.", nameof(value));
        }

        return uri;
    }

    private static void ValidateRequest(HttpExecutionRequest request)
    {
        if (request.ExpectedStatusCode is < 100 or > 599
            || request.TimeoutSeconds is < HttpMonitor.MinimumTimeoutSeconds or > HttpMonitor.MaximumTimeoutSeconds
            || !Enum.IsDefined(request.TextCondition)
            || (request.TextCondition == TextCondition.None && request.TextFragment is not null)
            || (request.TextCondition != TextCondition.None
                && (string.IsNullOrEmpty(request.TextFragment)
                    || request.TextFragment.EnumerateRunes().Count() > HttpMonitor.MaximumTextFragmentLength))
            || request.Headers.Count > 32)
        {
            throw new ArgumentException("The HTTP check configuration is invalid.", nameof(request));
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalBytes = 0;
        foreach (var header in request.Headers)
        {
            var name = HttpMonitorHeader.ValidateName(header.Name);
            var (_, secret) = HttpMonitorHeader.Create(Guid.NewGuid(), name, header.Value, DateTimeOffset.UnixEpoch);
            if (!names.Add(name))
            {
                throw new ArgumentException("HTTP header names must be unique.", nameof(request));
            }

            totalBytes += Encoding.ASCII.GetByteCount(name) + secret.ValueUtf8.Length;
        }

        if (totalBytes > MaximumHeaderBytes)
        {
            throw new ArgumentException("HTTP headers exceed their total size limit.", nameof(request));
        }
    }

    private static async Task<byte[]> ReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var body = new MemoryStream();
        var buffer = new byte[16 * 1_024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return body.ToArray();
            }

            if (body.Length + read > MaximumBodyBytes)
            {
                throw new ResponseTooLargeException();
            }

            await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static string Decode(byte[] value, string? declaredCharset)
    {
        try
        {
            var bom = Bom(value);
            var declared = DeclaredEncoding(declaredCharset, bom);
            if (declaredCharset is not null && bom.Encoding is not null && !declared.BomMatches)
            {
                throw new ResponseEncodingException();
            }

            var encoding = declared.Encoding ?? bom.Encoding ?? new UTF8Encoding(false, true);
            return encoding.GetString(value, bom.Length, value.Length - bom.Length);
        }
        catch (Exception exception) when (
            exception is ArgumentException or DecoderFallbackException)
        {
            throw new ResponseEncodingException(exception);
        }
    }

    private static (Encoding? Encoding, bool BomMatches) DeclaredEncoding(
        string? charset,
        (Encoding? Encoding, int Length, string? Kind) bom)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return (null, true);
        }

        var normalized = charset.Trim().Trim('"').ToLowerInvariant();
        return normalized switch
        {
            "utf-8" or "utf8" => (new UTF8Encoding(false, true), bom.Kind is null or "utf-8"),
            "utf-16" => (bom.Kind == "utf-16be" ? StrictUtf16(bigEndian: true) : StrictUtf16(bigEndian: false), bom.Kind is null or "utf-16le" or "utf-16be"),
            "utf-16le" or "unicode" => (StrictUtf16(bigEndian: false), bom.Kind is null or "utf-16le"),
            "utf-16be" => (StrictUtf16(bigEndian: true), bom.Kind is null or "utf-16be"),
            "utf-32" => (bom.Kind == "utf-32be" ? StrictUtf32(bigEndian: true) : StrictUtf32(bigEndian: false), bom.Kind is null or "utf-32le" or "utf-32be"),
            "utf-32le" => (StrictUtf32(bigEndian: false), bom.Kind is null or "utf-32le"),
            "utf-32be" => (StrictUtf32(bigEndian: true), bom.Kind is null or "utf-32be"),
            "iso-8859-1" or "latin1" or "latin-1" => (Encoding.Latin1, bom.Kind is null),
            _ => throw new ResponseEncodingException(),
        };
    }

    private static (Encoding? Encoding, int Length, string? Kind) Bom(byte[] value)
    {
        if (value.AsSpan().StartsWith(new byte[] { 0x00, 0x00, 0xfe, 0xff }))
        {
            return (StrictUtf32(bigEndian: true), 4, "utf-32be");
        }

        if (value.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0x00, 0x00 }))
        {
            return (StrictUtf32(bigEndian: false), 4, "utf-32le");
        }

        if (value.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
        {
            return (new UTF8Encoding(false, true), 3, "utf-8");
        }

        if (value.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }))
        {
            return (StrictUtf16(bigEndian: true), 2, "utf-16be");
        }

        if (value.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }))
        {
            return (StrictUtf16(bigEndian: false), 2, "utf-16le");
        }

        return (null, 0, null);
    }

    private static Encoding StrictUtf16(bool bigEndian) => new UnicodeEncoding(bigEndian, false, true);

    private static Encoding StrictUtf32(bool bigEndian) => new UTF32Encoding(bigEndian, false, true);

    private static bool SameOrigin(Uri first, Uri second) =>
        first.Scheme.Equals(second.Scheme, StringComparison.OrdinalIgnoreCase)
        && first.IdnHost.Equals(second.IdnHost, StringComparison.OrdinalIgnoreCase)
        && first.Port == second.Port;

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently
        or HttpStatusCode.Found
        or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;

    private static string Redacted(Uri target) => target.GetLeftPart(UriPartial.Path);

    private static HttpExecutionResult Failure(
        string code,
        string message,
        long started,
        int? statusCode = null,
        Uri? effectiveUrl = null) => new(
            false,
            code,
            message,
            statusCode,
            ElapsedMilliseconds(started),
            effectiveUrl is null ? null : Redacted(effectiveUrl));

    private static int ElapsedMilliseconds(long started) =>
        (int)Math.Min(int.MaxValue, Stopwatch.GetElapsedTime(started).TotalMilliseconds);

    private static bool HasTlsFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is AuthenticationException)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class TargetNotAllowedException : Exception;
    private sealed class HostResolutionException(Exception? inner = null) : Exception(null, inner);
    private sealed class ResponseTooLargeException : Exception;
    private sealed class ResponseEncodingException(Exception? inner = null) : Exception(null, inner);
}
