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
        DeploymentSecrets.ValidateBootstrapSource(configuration);
        await using var scope = scopeFactory.CreateAsyncScope();
        var arm = scope.ServiceProvider.GetRequiredService<ArmBootstrap>();
        var read = scope.ServiceProvider.GetRequiredService<ReadBootstrapState>();
        var state = await read.ExecuteAsync(cancellationToken);

        if (!state.Required)
        {
            logger.LogInformation("Bootstrap is closed; the instance already has its operator.");
            return;
        }

        await arm.ExecuteAsync(
            new BootstrapSettings(DeploymentSecrets.BootstrapProof(configuration)),
            cancellationToken);
        state = await read.ExecuteAsync(cancellationToken);
        if (state.Available)
        {
            logger.LogInformation("Bootstrap is armed for a bounded one-time exchange.");
        }
        else
        {
            logger.LogWarning(
                "The instance needs bootstrap, but no bootstrap proof is configured.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
