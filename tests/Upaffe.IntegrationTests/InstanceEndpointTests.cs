using System.Net;
using System.Net.Http.Json;
using Upaffe.Api.Http;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class InstanceEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Version_is_a_public_generated_client_operation()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();
        using var response = await client.GetAsync(
            "/api/version", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VersionResponse>(
            TestContext.Current.CancellationToken);
        Assert.Equal("0.0.0-dev", body?.Version);
        Assert.Equal(body?.Version, response.Headers.GetValues(VersionHeader.Name).Single());
    }

    [Fact]
    public async Task Every_response_carries_the_instance_version()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();
        using var response = await client.GetAsync(
            "/api/no-such-operation", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("0.0.0-dev", response.Headers.GetValues(VersionHeader.Name).Single());
    }
}
