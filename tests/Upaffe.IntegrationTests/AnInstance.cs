using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.IntegrationTests;

internal sealed class AnInstance(string connectionString) : WebApplicationFactory<Program>
{
    public static async Task<AnInstance> StartedAsync(PostgresFixture postgres) =>
        new(await postgres.CreateDatabaseAsync());

    public static AnInstance Against(string connectionString) => new(connectionString);

    public AnInstance StartedAgain() => new(connectionString);

    public static UpaffeDbContext ContextFor(string connectionString) =>
        new(new DbContextOptionsBuilder<UpaffeDbContext>().UseNpgsql(connectionString).Options);

    public static SchemaMigrator MigratorFor(UpaffeDbContext context) =>
        new(context, NullLogger<SchemaMigrator>.Instance);

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = connectionString,
            }));
        return base.CreateHost(builder);
    }
}
