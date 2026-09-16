using Upaffe.Application.Access;

namespace Upaffe.Api.Hosting;

/// <summary>Arms the short-lived bootstrap proof after the schema is current.</summary>
public sealed class BootstrapService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<BootstrapService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var arm = scope.ServiceProvider.GetRequiredService<ArmBootstrap>();
        var read = scope.ServiceProvider.GetRequiredService<ReadBootstrapState>();
        await arm.ExecuteAsync(
            new BootstrapSettings(configuration[BootstrapSettings.Variable]),
            cancellationToken);
        var state = await read.ExecuteAsync(cancellationToken);

        if (!state.Required)
        {
            logger.LogInformation("Bootstrap is closed; the instance already has its operator.");
        }
        else if (state.Available)
        {
            logger.LogInformation("Bootstrap is armed for a bounded one-time exchange.");
        }
        else
        {
            logger.LogWarning(
                "The instance needs bootstrap, but {Variable} is not configured.",
                BootstrapSettings.Variable);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
