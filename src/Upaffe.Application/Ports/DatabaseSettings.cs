namespace Upaffe.Application.Ports;

/// <summary>The PostgreSQL connection and the safe way to name it.</summary>
public sealed record DatabaseSettings(string ConnectionString)
{
    public const string ConnectionStringName = "Postgres";
    public const string Variable = "ConnectionStrings__" + ConnectionStringName;

    // A connection string can quote semicolons inside passwords. Do not attempt
    // partial parsing when producing ordinary diagnostic text.
    public string Redacted => "PostgreSQL connection configured;Password=***";

    public override string ToString() => Redacted;

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

    private static IEnumerable<(string Key, string Value)> Pairs(string connectionString) =>
        connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(part =>
            part.IndexOf('=') is var separator and >= 0
                ? (Normalized(part[..separator]), part[(separator + 1)..].Trim())
                : (Normalized(part), string.Empty));

    private static string Normalized(string keyword) =>
        keyword.Trim().Replace("_", " ", StringComparison.Ordinal).ToLowerInvariant();
}
