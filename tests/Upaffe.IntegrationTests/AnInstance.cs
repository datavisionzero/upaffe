using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.IntegrationTests;

internal sealed class AnInstance(
    string connectionString,
    IReadOnlyDictionary<string, string?>? settings = null,
    TimeProvider? clock = null,
    ILoggerProvider? logProvider = null) : WebApplicationFactory<Program>
{
    public static async Task<AnInstance> StartedAsync(
        PostgresFixture postgres,
        IReadOnlyDictionary<string, string?>? settings = null,
        TimeProvider? clock = null,
        ILoggerProvider? logProvider = null) =>
        new(await postgres.CreateDatabaseAsync(), settings, clock, logProvider);

    public static AnInstance Against(
        string connectionString,
        IReadOnlyDictionary<string, string?>? settings = null,
        TimeProvider? clock = null,
        ILoggerProvider? logProvider = null) =>
        new(connectionString, settings, clock, logProvider);

    public AnInstance StartedAgain() => new(connectionString, settings, clock, logProvider);

    public static UpaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<UpaffeDbContext>().UseNpgsql(connectionString).Options);

    public static SchemaMigrator MigratorFor(UpaffeDbContext context) =>
        new(context, NullLogger<SchemaMigrator>.Instance);

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration =>
        {
            var values = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = connectionString,
            };
            if (settings is not null)
            {
                foreach (var (key, value) in settings)
                {
                    values[key] = value;
                }
            }

            configuration.AddInMemoryCollection(values);
        });
        builder.ConfigureServices(services =>
        {
            if (clock is not null)
            {
                services.AddSingleton(clock);
            }
        });
        if (logProvider is not null)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logProvider));
        }

        return base.CreateHost(builder);
    }
}
