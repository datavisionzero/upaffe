using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Upaffe.Application.Ports;
using Upaffe.Infrastructure.Persistence;
using Upaffe.Infrastructure.Security;

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
        services.AddScoped<IBootstrapStore, BootstrapStore>();
        services.AddScoped<IBrowserSessionStore, BrowserSessionStore>();
        services.AddScoped<IManagementCredentialStore, ManagementCredentialStore>();
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        return services;
    }
}
