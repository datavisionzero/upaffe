using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Upaffe.Application.Ports;
using Upaffe.Infrastructure.Monitoring;
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
        services.AddScoped<IProjectStore, ProjectStore>();
        services.AddScoped<IHttpMonitorStore, HttpMonitorStore>();
        services.AddScoped<IPushMonitorStore, PushMonitorStore>();
        services.AddScoped<IReportingCredentialStore, ReportingCredentialStore>();
        services.AddScoped<IHttpMonitorHistoryStore, HttpMonitorHistoryStore>();
        services.AddScoped<IScheduledHttpCheckStore, ScheduledHttpCheckStore>();
        services.AddSingleton<IHostResolver, SystemHostResolver>();
        services.AddSingleton<IPinnedConnectionFactory, SocketPinnedConnectionFactory>();
        services.AddSingleton<IHttpCheckExecutor, HttpCheckExecutor>();
        services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        return services;
    }
}
