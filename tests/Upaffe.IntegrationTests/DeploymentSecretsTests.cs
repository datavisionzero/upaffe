using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Upaffe.Api.Hosting;
using Upaffe.Api.Http;
using Upaffe.Application.Access;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class DeploymentSecretsTests
{
    [Fact]
    public void Database_password_file_is_used_without_exposing_its_value()
    {
        var path = TemporaryFile("a;quoted=password\n");
        try
        {
            var settings = DeploymentSecrets.Database(Configuration(new()
            {
                [DeploymentSecrets.PostgresPasswordFile] = path,
                ["UPAFFE_DB_HOST"] = "db",
            }));
            var parsed = new NpgsqlConnectionStringBuilder(settings.ConnectionString);
            Assert.Equal("a;quoted=password", parsed.Password);
            Assert.Equal("db", parsed.Host);
            Assert.DoesNotContain("a;quoted=password", settings.Redacted, StringComparison.Ordinal);
            Assert.DoesNotContain("a;quoted=password", settings.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(path, settings.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_conflicting_and_invalid_secret_files_fail_without_path_or_value()
    {
        var path = TemporaryFile("sensitive-value\n");
        try
        {
            var conflict = Assert.Throws<InvalidOperationException>(() =>
                DeploymentSecrets.Database(Configuration(new()
                {
                    [DeploymentSecrets.PostgresPasswordFile] = path,
                    ["ConnectionStrings:Postgres"] = "Host=db;Password=another-secret",
                })));
            Assert.Contains("not both", conflict.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(path, conflict.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("another-secret", conflict.Message, StringComparison.Ordinal);

            var missing = Assert.Throws<InvalidOperationException>(() =>
                DeploymentSecrets.Database(Configuration(new()
                {
                    [DeploymentSecrets.PostgresPasswordFile] = path + ".missing",
                })));
            Assert.Contains("missing", missing.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(path, missing.Message, StringComparison.Ordinal);

            var unreadable = Assert.Throws<InvalidOperationException>(() =>
                DeploymentSecrets.Database(Configuration(new()
                {
                    [DeploymentSecrets.PostgresPasswordFile] = Path.GetDirectoryName(path),
                })));
            Assert.Contains("cannot be read", unreadable.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(path, unreadable.Message, StringComparison.Ordinal);

            File.WriteAllText(path, new string('x', 1025));
            var invalid = Assert.Throws<InvalidOperationException>(() =>
                DeploymentSecrets.Database(Configuration(new()
                {
                    [DeploymentSecrets.PostgresPasswordFile] = path,
                })));
            Assert.Contains("at most 1024", invalid.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(path, invalid.Message, StringComparison.Ordinal);

            File.WriteAllBytes(path, [0xff]);
            var malformed = Assert.Throws<InvalidOperationException>(() =>
                DeploymentSecrets.Database(Configuration(new()
                {
                    [DeploymentSecrets.PostgresPasswordFile] = path,
                })));
            Assert.Contains("cannot be read", malformed.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(path, malformed.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static string TemporaryFile(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"upaffe-secret-test-{Guid.NewGuid():N}");
        File.WriteAllText(path, contents);
        return path;
    }
}
