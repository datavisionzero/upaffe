using Upaffe.Infrastructure.Persistence;

namespace Upaffe.Api.Hosting;

/// <summary>Completes schema migration before the HTTP server becomes ready.</summary>
public sealed class SchemaMigrationService(
    IServiceScopeFactory scopeFactory,
    ILogger<SchemaMigrationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var migrator = scope.ServiceProvider.GetRequiredService<SchemaMigrator>();
        try
        {
            await migrator.ApplyAsync(cancellationToken);
        }
        catch (SchemaIsNewerException exception)
        {
            logger.LogCritical("{Reason} The instance will not start.", exception.Message);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Start was cancelled before the schema was checked.");
            throw;
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Migration failed; the instance will not start.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
