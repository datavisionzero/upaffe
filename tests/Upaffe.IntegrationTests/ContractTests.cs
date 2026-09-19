using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Upaffe.IntegrationTests;

/// <summary>Compares the running API with the checked-in source of both clients.</summary>
[Collection(nameof(PostgresCollection))]
public sealed class ContractTests(PostgresFixture postgres)
{
    private const string CaptureVariable = "UPAFFE_CAPTURE_CONTRACT";

    [Fact]
    public async Task The_document_is_public_and_names_the_technical_operations()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();
        using var response = await client.GetAsync(
            "/api/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal("upaffe", document["info"]!["title"]!.GetValue<string>());
        Assert.Null(document["servers"]);
        Assert.Equal(
            [
                "/api/bootstrap",
                "/api/email/default-recipients",
                "/api/email/deliveries",
                "/api/email/deliveries/summary",
                "/api/email/incidents/{incidentId}",
                "/api/email/password",
                "/api/email/settings",
                "/api/email/test",
                "/api/health/live",
                "/api/health/ready",
                "/api/management-credentials",
                "/api/management-credentials/{id}",
                "/api/management-credentials/{id}/rotate",
                "/api/projects",
                "/api/projects/{key}",
                "/api/projects/{key}/email-summary",
                "/api/projects/{key}/recipients",
                "/api/projects/{key}/restore",
                "/api/projects/{projectKey}/http-monitors",
                "/api/projects/{projectKey}/http-monitors/{monitorKey}",
                "/api/projects/{projectKey}/http-monitors/{monitorKey}/checks",
                "/api/projects/{projectKey}/http-monitors/{monitorKey}/headers/{name}",
                "/api/projects/{projectKey}/http-monitors/{monitorKey}/incidents",
                "/api/projects/{projectKey}/http-monitors/{monitorKey}/maintenance",
                "/api/projects/{projectKey}/http-monitors/{monitorKey}/pause",
                "/api/projects/{projectKey}/http-monitors/{monitorKey}/resume",
                "/api/projects/{projectKey}/http-monitors/{monitorKey}/test",
                "/api/projects/{projectKey}/maintenance",
                "/api/projects/{projectKey}/push-monitors",
                "/api/projects/{projectKey}/push-monitors/{monitorKey}",
                "/api/projects/{projectKey}/push-monitors/{monitorKey}/incidents",
                "/api/projects/{projectKey}/push-monitors/{monitorKey}/maintenance",
                "/api/projects/{projectKey}/push-monitors/{monitorKey}/pause",
                "/api/projects/{projectKey}/push-monitors/{monitorKey}/reporting-credential",
                "/api/projects/{projectKey}/push-monitors/{monitorKey}/reporting-credential/rotate",
                "/api/projects/{projectKey}/push-monitors/{monitorKey}/reports",
                "/api/projects/{projectKey}/push-monitors/{monitorKey}/resume",
                "/api/report/{secret}",
                "/api/reports",
                "/api/session",
                "/api/version",
            ],
            document["paths"]!.AsObject().Select(path => path.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_running_document_equals_the_checked_in_contract()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = instance.CreateClient();
        var served = JsonNode.Parse(
            await client.GetStringAsync(
                "/api/openapi/v1.json", TestContext.Current.CancellationToken))!;
        var path = Path.Combine(RepositoryRoot.Path, "docs", "api", "openapi.json");

        if (Environment.GetEnvironmentVariable(CaptureVariable) is "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(
                path, Formatted(served), TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(path), $"{path} is missing; capture it with {CaptureVariable}=1.");
        var checkedIn = JsonNode.Parse(
            await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.True(
            JsonNode.DeepEquals(served, checkedIn),
            $"The API and docs/api/openapi.json differ; recapture with {CaptureVariable}=1.");
    }

    private static string Formatted(JsonNode document) =>
        document.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + "\n";
}
