using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Upaffe.Application.Ports;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.IntegrationTests;

internal sealed class AnInstance(
    string connectionString,
    IReadOnlyDictionary<string, string?>? settings = null,
    TimeProvider? clock = null,
    ILoggerProvider? logProvider = null,
    IHttpCheckExecutor? checkExecutor = null,
    bool fileBackedDatabase = false,
    IPAddress? remoteAddress = null,
    Action<IServiceCollection>? serviceOverrides = null) : WebApplicationFactory<Program>
{
    public static async Task<AnInstance> StartedAsync(
        PostgresFixture postgres,
        IReadOnlyDictionary<string, string?>? settings = null,
        TimeProvider? clock = null,
        ILoggerProvider? logProvider = null,
        IHttpCheckExecutor? checkExecutor = null) =>
        new(await postgres.CreateDatabaseAsync(), settings, clock, logProvider, checkExecutor);

    public static AnInstance Against(
        string connectionString,
        IReadOnlyDictionary<string, string?>? settings = null,
        TimeProvider? clock = null,
        ILoggerProvider? logProvider = null,
        IHttpCheckExecutor? checkExecutor = null,
        IPAddress? remoteAddress = null,
        Action<IServiceCollection>? serviceOverrides = null) =>
        new(connectionString, settings, clock, logProvider, checkExecutor,
            remoteAddress: remoteAddress, serviceOverrides: serviceOverrides);

    public static AnInstance AgainstFileBackedDatabase(
        string connectionString,
        IReadOnlyDictionary<string, string?> settings,
        ILoggerProvider? logProvider = null) =>
        new(connectionString, settings, logProvider: logProvider, fileBackedDatabase: true);

    public AnInstance StartedAgain() =>
        new(connectionString, settings, clock, logProvider, checkExecutor,
            fileBackedDatabase, remoteAddress, serviceOverrides);

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
                ["Monitoring:Enabled"] = "false",
                ["HistoryRetention:Enabled"] = "false",
                ["EmailDelivery:Enabled"] = "false",
            };
            if (!fileBackedDatabase)
            {
                values["ConnectionStrings:Postgres"] = connectionString;
            }
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
            if (remoteAddress is not null)
            {
                services.AddSingleton<IStartupFilter>(new RemoteAddressStartupFilter(remoteAddress));
            }

            if (clock is not null)
            {
                services.AddSingleton(clock);
            }

            if (checkExecutor is not null)
            {
                services.RemoveAll<IHttpCheckExecutor>();
                services.AddSingleton(checkExecutor);
            }

            serviceOverrides?.Invoke(services);
        });
        if (logProvider is not null)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logProvider));
        }

        return base.CreateHost(builder);
    }
}

internal sealed class RemoteAddressStartupFilter(IPAddress address) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, following) =>
        {
            context.Connection.RemoteIpAddress = address;
            return following();
        });
        next(app);
    };
}
