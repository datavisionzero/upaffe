namespace Upaffe.Application.Ports;

/// <summary>The PostgreSQL connection and the safe way to name it.</summary>
public sealed record DatabaseSettings(string ConnectionString)
{
    public const string ConnectionStringName = "Postgres";
    public const string Variable = "ConnectionStrings__" + ConnectionStringName;

    private static readonly string[] Secret =
        ["password", "pwd", "sslpassword", "ssl password", "ssl key password"];

    public string Redacted { get; } = Redact(ConnectionString);

    public static DatabaseSettings FromConnectionString(string? connectionString)
    {
        var value = (connectionString ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            throw new ArgumentException(
                $"{Variable} is not set. It must name the PostgreSQL database for this instance.");
        }

        if (!Pairs(value).Any(pair => IsHost(pair.Key)))
        {
            throw new ArgumentException(
                $"{Variable} names no host. Provide keyword=value pairs including Host= or Server=.");
        }

        return new DatabaseSettings(value);
    }

    private static bool IsHost(string keyword) =>
        keyword is "host" or "server" or "data source" or "datasource";

    private static string Redact(string connectionString) =>
        string.Join(
            ';',
            connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(part =>
            {
                var separator = part.IndexOf('=');
                if (separator < 0)
                {
                    return part;
                }

                return Secret.Contains(Normalized(part[..separator]))
                    ? $"{part[..separator].Trim()}=***"
                    : part.Trim();
            }));

    private static IEnumerable<(string Key, string Value)> Pairs(string connectionString) =>
        connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(part =>
            part.IndexOf('=') is var separator and >= 0
                ? (Normalized(part[..separator]), part[(separator + 1)..].Trim())
                : (Normalized(part), string.Empty));

    private static string Normalized(string keyword) =>
        keyword.Trim().Replace("_", " ", StringComparison.Ordinal).ToLowerInvariant();
}
