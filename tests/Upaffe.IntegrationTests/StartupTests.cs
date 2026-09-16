namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class StartupTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_host_without_database_configuration_does_not_start()
    {
        await using var instance = AnInstance.Against(string.Empty);
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(
            () => instance.CreateClient(), TestContext.Current.CancellationToken));
        Assert.Contains(
            "ConnectionStrings__Postgres is not set",
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_restarts_against_the_same_migrated_database()
    {
        await using var first = await AnInstance.StartedAsync(postgres);
        using (var client = first.CreateClient())
        {
            using var ready = await client.GetAsync(
                "/api/health/ready", TestContext.Current.CancellationToken);
            ready.EnsureSuccessStatusCode();
        }

        await using var again = first.StartedAgain();
        using var restarted = again.CreateClient();
        using var response = await restarted.GetAsync(
            "/api/health/ready", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
