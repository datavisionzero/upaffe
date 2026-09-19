using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Api.Http;
using Upaffe.Application.Access;
using Upaffe.Domain.Projects;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class MaintenanceTests(PostgresFixture postgres)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Fact]
    public async Task Simultaneous_starts_respect_the_single_scope_version()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = await postgres.CreateDatabaseAsync();
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        await using (var setup = AnInstance.ContextFor(connection))
        {
            await AnInstance.MigratorFor(setup).ApplyAsync(ct);
            setup.Projects.Add(Project.Create("systems", "Systems", now));
            await setup.SaveChangesAsync(ct);
        }
        await using var firstContext = AnInstance.ContextFor(connection);
        await using var secondContext = AnInstance.ContextFor(connection);
        var results = await Task.WhenAll(
            new MaintenanceStore(firstContext).StartAsync("project", "systems", null,
                0, TimeSpan.FromMinutes(10), now, ct),
            new MaintenanceStore(secondContext).StartAsync("project", "systems", null,
                0, TimeSpan.FromMinutes(10), now, ct));
        Assert.Contains(results, value => value.Outcome == MaintenanceMutation.Changed);
        Assert.Contains(results, value => value.Outcome == MaintenanceMutation.VersionConflict);
    }

    [Fact]
    public async Task Project_and_monitor_windows_overlap_end_independently_and_expire_across_restart()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = await postgres.CreateDatabaseAsync();
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
        await using var instance = AnInstance.Against(connection,
            new Dictionary<string, string?>
            { [BootstrapSettings.Variable] = "a-valid-bootstrap-proof-for-maintenance" }, clock);
        using var client = instance.CreateClient(new WebApplicationFactoryClientOptions
        { HandleCookies = false });
        using var bootstrap = await client.PostAsJsonAsync("/api/bootstrap",
            new BootstrapRequest("a-valid-bootstrap-proof-for-maintenance",
                "operator@example.test", "a long operator password"), Json, ct);
        Assert.Equal(HttpStatusCode.NoContent, bootstrap.StatusCode);
        using var signin = await client.PostAsJsonAsync("/api/session",
            new SignInRequest("operator@example.test", "a long operator password"), Json, ct);
        var cookie = Assert.Single(signin.Headers.GetValues("Set-Cookie")).Split(';')[0];
        using var createCredential = new HttpRequestMessage(HttpMethod.Post, "/api/management-credentials")
        { Content = JsonContent.Create(new CreateCredentialRequest("maintenance test agent"), options: Json) };
        createCredential.Headers.Add("Cookie", cookie);
        createCredential.Headers.Add(CsrfProtection.Header, "1");
        createCredential.Headers.Add("Origin", "http://localhost");
        using var issuedResponse = await client.SendAsync(createCredential, ct);
        var issued = await issuedResponse.Content.ReadFromJsonAsync<IssuedCredentialResponse>(Json, ct);
        Assert.NotNull(issued);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued.Token);

        using var project = await client.PostAsJsonAsync("/api/projects",
            new CreateProjectRequest("systems", "Systems"), Json, ct);
        Assert.Equal(HttpStatusCode.Created, project.StatusCode);
        using var http = await client.PostAsJsonAsync("/api/projects/systems/http-monitors",
            new CreateHttpMonitorRequest("site", "Site", "https://site.example.test/health",
                200, "none", null, 300, 10, 2, null, null, null), Json, ct);
        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        using var push = await client.PostAsJsonAsync("/api/projects/systems/push-monitors",
            new CreatePushMonitorRequest("backup", "Backup", "job_completion", 3600, 60,
                null, null), Json, ct);
        Assert.Equal(HttpStatusCode.Created, push.StatusCode);

        using var invalid = await client.PostAsJsonAsync("/api/projects/systems/maintenance",
            new StartMaintenanceRequest(0, 30), Json, ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var projectStart = await client.PostAsJsonAsync("/api/projects/systems/maintenance",
            new StartMaintenanceRequest(0, 1200), Json, ct);
        Assert.Equal(HttpStatusCode.OK, projectStart.StatusCode);
        var projectWindow = await Data(projectStart, ct);
        Assert.True(projectWindow.GetProperty("direct_active").GetBoolean());
        Assert.Equal(1, projectWindow.GetProperty("version").GetInt64());

        using var inherited = await client.GetAsync(
            "/api/projects/systems/push-monitors/backup/maintenance", ct);
        var pushStatus = await Data(inherited, ct);
        Assert.True(pushStatus.GetProperty("effective_active").GetBoolean());
        Assert.False(pushStatus.GetProperty("direct_active").GetBoolean());
        Assert.Equal("project", pushStatus.GetProperty("active_scopes")[0].GetString());

        using var monitorStart = await client.PostAsJsonAsync(
            "/api/projects/systems/http-monitors/site/maintenance",
            new StartMaintenanceRequest(0, 1800), Json, ct);
        Assert.Equal(HttpStatusCode.OK, monitorStart.StatusCode);
        var monitorWindow = await Data(monitorStart, ct);
        Assert.Equal(2, monitorWindow.GetProperty("active_scopes").GetArrayLength());
        Assert.Equal(clock.GetUtcNow().AddMinutes(30),
            monitorWindow.GetProperty("effective_ends_at").GetDateTimeOffset());

        using var stale = await client.PostAsJsonAsync(
            "/api/projects/systems/http-monitors/site/maintenance",
            new StartMaintenanceRequest(0, 2400), Json, ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var extended = await client.PostAsJsonAsync(
            "/api/projects/systems/http-monitors/site/maintenance",
            new StartMaintenanceRequest(1, 2400), Json, ct);
        Assert.Equal(HttpStatusCode.OK, extended.StatusCode);
        Assert.Equal(2, (await Data(extended, ct)).GetProperty("version").GetInt64());

        using var endProject = await client.DeleteAsync(
            "/api/projects/systems/maintenance?version=1", ct);
        Assert.Equal(HttpStatusCode.OK, endProject.StatusCode);
        using var directOnly = await client.GetAsync(
            "/api/projects/systems/http-monitors/site/maintenance", ct);
        var directStatus = await Data(directOnly, ct);
        Assert.True(directStatus.GetProperty("effective_active").GetBoolean());
        Assert.Equal("http", directStatus.GetProperty("active_scopes")[0].GetString());

        clock.Advance(TimeSpan.FromMinutes(41));
        await using var restarted = instance.StartedAgain();
        using var afterRestart = restarted.CreateClient(new WebApplicationFactoryClientOptions
        { HandleCookies = false });
        afterRestart.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued.Token);
        using var expired = await afterRestart.GetAsync(
            "/api/projects/systems/http-monitors/site/maintenance", ct);
        Assert.False((await Data(expired, ct)).GetProperty("effective_active").GetBoolean());
        using var newWindow = await afterRestart.PostAsJsonAsync(
            "/api/projects/systems/http-monitors/site/maintenance",
            new StartMaintenanceRequest(2, 600), Json, ct);
        Assert.Equal(HttpStatusCode.OK, newWindow.StatusCode);
        Assert.Equal(3, (await Data(newWindow, ct)).GetProperty("version").GetInt64());
        await using var inspect = AnInstance.ContextFor(connection);
        Assert.Equal(3, await inspect.MaintenanceWindows.CountAsync(ct));

        using var anonymous = restarted.CreateClient(new WebApplicationFactoryClientOptions
        { HandleCookies = false });
        using var unauthorized = await anonymous.GetAsync("/api/projects/systems/maintenance", ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    private static async Task<JsonElement> Data(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
