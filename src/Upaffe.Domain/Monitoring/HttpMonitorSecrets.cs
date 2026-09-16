using System.Text;
using System.Text.RegularExpressions;

namespace Upaffe.Domain.Monitoring;

/// <summary>Write-only target material kept outside ordinary monitor reads.</summary>
public sealed class HttpMonitorSecret
{
    private HttpMonitorSecret()
    {
    }

    private HttpMonitorSecret(Guid monitorId, byte[]? targetQueryUtf8)
    {
        MonitorId = monitorId;
        TargetQueryUtf8 = targetQueryUtf8;
    }

    public Guid MonitorId { get; private set; }
    public byte[]? TargetQueryUtf8 { get; private set; }

    public static HttpMonitorSecret FromTarget(Guid monitorId, string targetUrl)
    {
        if (monitorId == Guid.Empty)
        {
            throw new ArgumentException("A monitor is required.", nameof(monitorId));
        }

        HttpMonitor.NormalizeTarget(targetUrl, out var query);
        return new(monitorId, query is null ? null : Encoding.UTF8.GetBytes(query));
    }

    public void ReplaceTarget(string targetUrl)
    {
        HttpMonitor.NormalizeTarget(targetUrl, out var query);
        TargetQueryUtf8 = query is null ? null : Encoding.UTF8.GetBytes(query);
    }

    public string? RevealTargetQuery() =>
        TargetQueryUtf8 is null ? null : new UTF8Encoding(false, true).GetString(TargetQueryUtf8);
}

/// <summary>Non-secret metadata for one configured HTTP request header.</summary>
public sealed partial class HttpMonitorHeader
{
    public const int MaximumNameLength = 128;

    private static readonly HashSet<string> ForbiddenNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "connection",
        "content-length",
        "host",
        "keep-alive",
        "proxy-authenticate",
        "proxy-authorization",
        "te",
        "trailer",
        "transfer-encoding",
        "upgrade",
    };

    private HttpMonitorHeader()
    {
    }

    private HttpMonitorHeader(Guid monitorId, string name, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        MonitorId = monitorId;
        Name = ValidateName(name);
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid MonitorId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static (HttpMonitorHeader Header, HttpMonitorHeaderSecret Secret) Create(
        Guid monitorId,
        string name,
        string value,
        DateTimeOffset now)
    {
        if (monitorId == Guid.Empty)
        {
            throw new ArgumentException("A monitor is required.", nameof(monitorId));
        }

        var header = new HttpMonitorHeader(monitorId, name, now);
        return (header, HttpMonitorHeaderSecret.Create(header.Id, value));
    }

    public void Touch(DateTimeOffset now) => UpdatedAt = now;

    public static string ValidateName(string value)
    {
        var name = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (name.Length is < 1 or > MaximumNameLength
            || !HeaderNamePattern().IsMatch(name)
            || ForbiddenNames.Contains(name))
        {
            throw new ArgumentException("The HTTP header name is invalid or managed by upaffe.", nameof(value));
        }

        return name;
    }

    [GeneratedRegex("^[!#$%&'*+.^_`|~0-9a-z-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderNamePattern();
}

/// <summary>Explicitly accessed secret value for one HTTP request header.</summary>
public sealed class HttpMonitorHeaderSecret
{
    public const int MaximumValueBytes = 4 * 1_024;

    private HttpMonitorHeaderSecret()
    {
    }

    private HttpMonitorHeaderSecret(Guid headerId, byte[] valueUtf8)
    {
        HeaderId = headerId;
        ValueUtf8 = valueUtf8;
    }

    public Guid HeaderId { get; private set; }
    public byte[] ValueUtf8 { get; private set; } = [];

    internal static HttpMonitorHeaderSecret Create(Guid headerId, string value) =>
        new(headerId, Encode(value));

    public void Replace(string value) => ValueUtf8 = Encode(value);

    public string Reveal() => new UTF8Encoding(false, true).GetString(ValueUtf8);

    private static byte[] Encode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Any(char.IsControl))
        {
            throw new ArgumentException("An HTTP header value cannot contain control characters.", nameof(value));
        }

        var bytes = new UTF8Encoding(false, true).GetBytes(value);
        if (bytes.Length > MaximumValueBytes)
        {
            throw new ArgumentException("An HTTP header value may contain at most 4 KiB of UTF-8.", nameof(value));
        }

        return bytes;
    }
}
