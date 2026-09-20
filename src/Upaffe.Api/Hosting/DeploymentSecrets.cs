using System.Text;
using Npgsql;
using Upaffe.Application.Ports;

namespace Upaffe.Api.Hosting;

/// <summary>Reads mounted deployment secrets without exposing values or paths in failures.</summary>
public static class DeploymentSecrets
{
    public const string PostgresPasswordFile = "UPAFFE_POSTGRES_PASSWORD_FILE";
    public const string HeartbeatUrlFile = "UPAFFE_HEARTBEAT_URL_FILE";

    public static DatabaseSettings Database(IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString(DatabaseSettings.ConnectionStringName);
        var passwordPath = configuration[PostgresPasswordFile];
        if (connection is not null && passwordPath is not null)
        {
            throw new InvalidOperationException(
                $"Configure either {DatabaseSettings.Variable} or {PostgresPasswordFile}, not both.");
        }

        if (passwordPath is null)
        {
            return DatabaseSettings.FromConnectionString(connection);
        }

        var password = ReadFile(PostgresPasswordFile, passwordPath, 1024);
        var portText = configuration["UPAFFE_DB_PORT"];
        if (portText is not null
            && (!int.TryParse(portText, out var port) || port is < 1 or > 65535))
        {
            throw new InvalidOperationException("UPAFFE_DB_PORT must be a TCP port from 1 to 65535.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Coordinate(configuration, "UPAFFE_DB_HOST", "db"),
            Port = portText is null ? 5432 : int.Parse(portText),
            Database = Coordinate(configuration, "UPAFFE_DB_NAME", "upaffe"),
            Username = Coordinate(configuration, "UPAFFE_DB_USER", "upaffe"),
            Password = password,
        };
        return DatabaseSettings.FromConnectionString(builder.ConnectionString);
    }

    public static HeartbeatDestination Heartbeat(IConfiguration configuration)
    {
        var path = configuration[HeartbeatUrlFile];
        if (path is null)
        {
            return HeartbeatDestination.Disabled;
        }

        var value = ReadFile(HeartbeatUrlFile, path, 2048);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.HostNameType != UriHostNameType.Dns
            || !uri.Host.Contains('.', StringComparison.Ordinal)
            || uri.IsLoopback
            || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || uri.UserInfo.Length != 0
            || uri.Fragment.Length != 0)
        {
            throw new InvalidOperationException(
                $"{HeartbeatUrlFile} must contain an absolute HTTPS URL with a multi-label DNS name and no user info or fragment.");
        }

        return new HeartbeatDestination(uri);
    }

    public static string ReadFile(string settingName, string path, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096
            || path.Contains('\0') || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException($"{settingName} must name an absolute file path.");
        }

        try
        {
            using var reader = new StreamReader(
                path, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
            var buffer = new char[maximumLength + 3];
            var count = reader.ReadBlock(buffer, 0, buffer.Length);
            if (count == buffer.Length)
            {
                throw InvalidContent(settingName, maximumLength);
            }

            var value = new string(buffer, 0, count).TrimEnd('\r', '\n');
            if (value.Length == 0 || value.Length > maximumLength
                || count - value.Length > 2
                || value.Contains('\r') || value.Contains('\n') || value.Contains('\0'))
            {
                throw InvalidContent(settingName, maximumLength);
            }

            return value;
        }
        catch (FileNotFoundException)
        {
            throw new InvalidOperationException($"{settingName} file is missing.");
        }
        catch (DirectoryNotFoundException)
        {
            throw new InvalidOperationException($"{settingName} file is missing.");
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException or DecoderFallbackException)
        {
            throw new InvalidOperationException($"{settingName} file cannot be read.");
        }
    }

    private static string Coordinate(IConfiguration configuration, string name, string fallback)
    {
        var value = configuration[name] ?? fallback;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 255)
        {
            throw new InvalidOperationException($"{name} must contain 1-255 characters.");
        }

        return value;
    }

    private static InvalidOperationException InvalidContent(string name, int maximumLength) =>
        new($"{name} file must contain one nonempty UTF-8 line of at most {maximumLength} characters.");
}
