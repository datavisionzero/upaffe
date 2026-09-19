using System.Text.RegularExpressions;

namespace Upaffe.Domain.Notifications;

public sealed partial class EmailConfiguration
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public Guid Id { get; private set; } = SingletonId;
    public long Version { get; private set; }
    public string? Host { get; private set; }
    public int? Port { get; private set; }
    public string Security { get; private set; } = "starttls";
    public string? SenderAddress { get; private set; }
    public string? SenderName { get; private set; }
    public string? PublicBaseUrl { get; private set; }
    public string? Username { get; private set; }
    public string? Password { get; private set; }
    public string[] DefaultRecipients { get; private set; } = [];
    public DateTimeOffset UpdatedAt { get; private set; }

    private EmailConfiguration() { }

    public static EmailConfiguration Create(DateTimeOffset now) => new() { UpdatedAt = now };

    public void Update(string? host, int? port, string security, string? senderAddress,
        string? senderName, string? publicBaseUrl, string? username, DateTimeOffset now)
    {
        Host = NormalizeOptional(host, 253, "host");
        if (port is < 1 or > 65535) throw new ArgumentException("SMTP port must be 1-65535.", nameof(port));
        Port = port;
        Security = security is "none" or "starttls" or "tls"
            ? security : throw new ArgumentException("SMTP security must be none, starttls, or tls.", nameof(security));
        SenderAddress = senderAddress is null ? null : NormalizeAddress(senderAddress);
        SenderName = NormalizeOptional(senderName, 100, "senderName");
        Username = NormalizeOptional(username, 253, "username");
        if (publicBaseUrl is null || publicBaseUrl.Trim().Length == 0)
        {
            PublicBaseUrl = null;
        }
        else if (Uri.TryCreate(publicBaseUrl.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme is "https" or "http" && uri.UserInfo.Length == 0
            && uri.Query.Length == 0 && uri.Fragment.Length == 0)
        {
            PublicBaseUrl = uri.ToString().TrimEnd('/');
        }
        else throw new ArgumentException("Public base URL must be an absolute HTTP or HTTPS URL without credentials, query, or fragment.", nameof(publicBaseUrl));
        Changed(now);
    }

    public void SetPassword(string password, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length > 1024)
            throw new ArgumentException("SMTP password must be 1-1024 characters.", nameof(password));
        Password = password;
        Changed(now);
    }

    public void ClearPassword(DateTimeOffset now) { Password = null; Changed(now); }
    public void SetDefaultRecipients(string[] recipients, DateTimeOffset now)
    { DefaultRecipients = recipients; Changed(now); }

    private void Changed(DateTimeOffset now) { Version++; UpdatedAt = now; }

    private static string? NormalizeOptional(string? value, int maximum, string field)
    {
        var result = value?.Trim();
        if (result?.Length > maximum || result?.Contains('\r') == true || result?.Contains('\n') == true)
            throw new ArgumentException($"{field} is too long or contains a line break.", field);
        return string.IsNullOrEmpty(result) ? null : result;
    }

    public static string[] NormalizeRecipients(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count > 50) throw new ArgumentException("Provide at most 50 recipients.", nameof(values));
        var normalized = values.Select(NormalizeAddress).ToArray();
        if (normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
            throw new ArgumentException("Recipient addresses must be unique.", nameof(values));
        return normalized;
    }

    public static string NormalizeAddress(string value)
    {
        var address = (value ?? string.Empty).Trim();
        if (address.Length > 253 || !EmailPattern().IsMatch(address))
            throw new ArgumentException("A plain email address is required.", nameof(value));
        var at = address.LastIndexOf('@');
        var local = address[..at];
        if (local.StartsWith('.') || local.EndsWith('.') || local.Contains("..", StringComparison.Ordinal)
            || address[(at + 1)..].Split('.').Any(label => label.Length > 63))
            throw new ArgumentException("A plain email address is required.", nameof(value));
        return address[..at] + "@" + address[(at + 1)..].ToLowerInvariant();
    }

    [GeneratedRegex("^[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
