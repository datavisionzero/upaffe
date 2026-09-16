using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Upaffe.Application.Ports;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.Infrastructure;

public static class InfrastructureServices
{
    public static IServiceCollection AddUpaffeInfrastructure(
        this IServiceCollection services,
        DatabaseSettings database)
    {
        services.AddSingleton(database);
        services.AddDbContext<UpaffeDbContext>(options => options
            .UseNpgsql(database.ConnectionString)
            .ConfigureWarnings(warnings =>
                warnings.Log((RelationalEventId.CommandError, LogLevel.Debug))));
        services.AddScoped<SchemaMigrator>();
        return services;
    }
}
