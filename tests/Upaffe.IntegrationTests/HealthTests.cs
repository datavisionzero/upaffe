using System.Net;
using System.Net.Http.Json;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class HealthTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_started_host_is_live()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();
        using var response = await client.GetAsync(
            "/api/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "live",
            (await response.Content.ReadFromJsonAsync<Liveness>(
                TestContext.Current.CancellationToken))?.Status);
    }

    [Fact]
    public async Task Liveness_exposes_no_host_configuration()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();
        var body = await client.GetStringAsync(
            "/api/health/live", TestContext.Current.CancellationToken);

        Assert.Equal("{\"status\":\"live\"}", body);
    }

    [Fact]
    public async Task A_started_host_is_ready()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();
        using var response = await client.GetAsync(
            "/api/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "ready",
            (await response.Content.ReadFromJsonAsync<Liveness>(
                TestContext.Current.CancellationToken))?.Status);
    }

    private sealed record Liveness(string Status);
}
