using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Upaffe.Api.Http;
using Upaffe.Application.Access;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class ProjectTests(PostgresFixture postgres)
{
    private const string Email = "operator@example.test";
    private const string Password = "a long operator password";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public async Task A_project_keeps_its_identity_through_the_complete_management_lifecycle()
    {
        await using var instance = await EstablishedAsync();
        using var client = Client(instance);
        var browser = await SignInAsync(client);
        var token = await CredentialAsync(client, browser);

        Assert.Equal(
            "authentication_required",
            await ProblemCode(await client.GetAsync(
                "/api/projects",
                TestContext.Current.CancellationToken)));

        var created = await ProjectAsync(await JsonAsync(
            client,
            HttpMethod.Post,
            "/api/projects",
            new CreateProjectRequest("backup-jobs", " Backup jobs "),
            token));
        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal("backup-jobs", created.Project.Key);
        Assert.Equal("Backup jobs", created.Project.Name);
        Assert.Equal(1, created.Project.Version);

        var repeated = await ProjectAsync(await JsonAsync(
            client,
            HttpMethod.Post,
            "/api/projects",
            new CreateProjectRequest("backup-jobs", "Backup jobs"),
            token));
        Assert.Equal(HttpStatusCode.OK, repeated.Status);
        Assert.Equal(created.Project.Id, repeated.Project.Id);
        Assert.Equal(created.Project.Version, repeated.Project.Version);

        var live = await ProjectsAsync(await SendAsync(client, HttpMethod.Get, "/api/projects", token));
        Assert.Equal(created.Project.Id, Assert.Single(live).Id);

        var read = await ProjectAsync(await SendAsync(
            client,
            HttpMethod.Get,
            "/api/projects/backup-jobs",
            token));
        Assert.Equal(created.Project.Id, read.Project.Id);

        var renamed = await ProjectAsync(await JsonAsync(
            client,
            HttpMethod.Put,
            "/api/projects/backup-jobs",
            new RenameProjectRequest("Backups", created.Project.Version),
            token));
        Assert.Equal("Backups", renamed.Project.Name);
        Assert.Equal(2, renamed.Project.Version);

        using (var stale = await JsonAsync(
            client,
            HttpMethod.Put,
            "/api/projects/backup-jobs",
            new RenameProjectRequest("Stale name", created.Project.Version),
            token))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("conflict", await ProblemCode(stale));
        }

        var deleted = await ProjectAsync(await SendAsync(
            client,
            HttpMethod.Delete,
            $"/api/projects/backup-jobs?version={renamed.Project.Version}",
            token));
        Assert.Equal(3, deleted.Project.Version);
        Assert.NotNull(deleted.Project.DeletedAt);
        Assert.Empty(await ProjectsAsync(await SendAsync(
            client,
            HttpMethod.Get,
            "/api/projects",
            token)));
        Assert.Equal(created.Project.Id, Assert.Single(await ProjectsAsync(await SendAsync(
            client,
            HttpMethod.Get,
            "/api/projects?deleted=true",
            token))).Id);

        var repeatedWhileDeleted = await ProjectAsync(await JsonAsync(
            client,
            HttpMethod.Post,
            "/api/projects",
            new CreateProjectRequest("backup-jobs", "Backups"),
            token));
        Assert.Equal(HttpStatusCode.OK, repeatedWhileDeleted.Status);
        Assert.Equal(created.Project.Id, repeatedWhileDeleted.Project.Id);
        Assert.Equal(deleted.Project.Version, repeatedWhileDeleted.Project.Version);
        Assert.NotNull(repeatedWhileDeleted.Project.DeletedAt);

        using (var renameDeleted = await JsonAsync(
            client,
            HttpMethod.Put,
            "/api/projects/backup-jobs",
            new RenameProjectRequest("Cannot rename", deleted.Project.Version),
            token))
        {
            Assert.Equal(HttpStatusCode.Conflict, renameDeleted.StatusCode);
            Assert.Equal("conflict", await ProblemCode(renameDeleted));
        }

        var restored = await ProjectAsync(await JsonAsync(
            client,
            HttpMethod.Post,
            "/api/projects/backup-jobs/restore",
            new ProjectVersionRequest(deleted.Project.Version),
            token));
        Assert.Equal(created.Project.Id, restored.Project.Id);
        Assert.Equal(created.Project.Key, restored.Project.Key);
        Assert.Equal(4, restored.Project.Version);
        Assert.Null(restored.Project.DeletedAt);
    }

    [Fact]
    public async Task Invalid_and_duplicate_project_facts_have_stable_problem_contracts()
    {
        await using var instance = await EstablishedAsync();
        using var client = Client(instance);
        var browser = await SignInAsync(client);

        using (var invalidKey = await JsonAsync(
            client,
            HttpMethod.Post,
            "/api/projects",
            new CreateProjectRequest("Upper-case", "A project"),
            cookie: browser,
            csrf: true))
        {
            var problem = await Problem(invalidKey);
            Assert.Equal(HttpStatusCode.BadRequest, invalidKey.StatusCode);
            Assert.Equal("validation", problem?.Code);
            Assert.Contains("key", problem?.Errors?.Keys ?? []);
        }

        using (var invalidName = await JsonAsync(
            client,
            HttpMethod.Post,
            "/api/projects",
            new CreateProjectRequest("valid-key", " "),
            cookie: browser,
            csrf: true))
        {
            var problem = await Problem(invalidName);
            Assert.Equal(HttpStatusCode.BadRequest, invalidName.StatusCode);
            Assert.Equal("validation", problem?.Code);
            Assert.Contains("name", problem?.Errors?.Keys ?? []);
        }

        using (var first = await JsonAsync(
            client,
            HttpMethod.Post,
            "/api/projects",
            new CreateProjectRequest("stable-key", "First name"),
            cookie: browser,
            csrf: true))
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        using (var conflicting = await JsonAsync(
            client,
            HttpMethod.Post,
            "/api/projects",
            new CreateProjectRequest("stable-key", "Different name"),
            cookie: browser,
            csrf: true))
        {
            Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
            Assert.Equal("conflict", await ProblemCode(conflicting));
        }

        using var missing = await SendAsync(
            client,
            HttpMethod.Get,
            "/api/projects/not-there",
            cookie: browser);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("not_found", await ProblemCode(missing));
    }

    [Fact]
    public async Task Concurrent_create_and_change_are_idempotent_and_optimistic()
    {
        await using var instance = await EstablishedAsync();
        using var setup = Client(instance);
        var browser = await SignInAsync(setup);
        var token = await CredentialAsync(setup, browser);
        using var first = Client(instance);
        using var second = Client(instance);

        var creates = await Task.WhenAll(
            JsonAsync(first, HttpMethod.Post, "/api/projects", new CreateProjectRequest("racing", "Racing"), token),
            JsonAsync(second, HttpMethod.Post, "/api/projects", new CreateProjectRequest("racing", "Racing"), token));
        Assert.Contains(creates, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Contains(creates, response => response.StatusCode == HttpStatusCode.OK);
        var project = (await creates[0].Content.ReadFromJsonAsync<ProjectResponse>(
            Json,
            TestContext.Current.CancellationToken))!;
        foreach (var response in creates)
        {
            response.Dispose();
        }

        var changes = await Task.WhenAll(
            JsonAsync(first, HttpMethod.Put, "/api/projects/racing", new RenameProjectRequest("First", project.Version), token),
            JsonAsync(second, HttpMethod.Put, "/api/projects/racing", new RenameProjectRequest("Second", project.Version), token));
        Assert.Contains(changes, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Contains(changes, response => response.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in changes)
        {
            response.Dispose();
        }
    }

    private async Task<AnInstance> EstablishedAsync()
    {
        var instance = AnInstance.Against(await postgres.CreateDatabaseAsync());
        using var client = instance.CreateClient();
        await instance.EstablishAsync(Email, Password);
        return instance;
    }

    private static HttpClient Client(AnInstance instance) =>
        instance.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static async Task<string> SignInAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/session",
            new SignInRequest(Email, Password),
            TestContext.Current.CancellationToken);
        return Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';', 2)[0].Split('=', 2)[1];
    }

    private static async Task<string> CredentialAsync(HttpClient client, string browser)
    {
        using var response = await JsonAsync(
            client,
            HttpMethod.Post,
            "/api/management-credentials",
            new CreateCredentialRequest("project test agent"),
            cookie: browser,
            csrf: true);
        var issued = await response.Content.ReadFromJsonAsync<IssuedCredentialResponse>(
            Json,
            TestContext.Current.CancellationToken);
        return issued!.Token;
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string? bearer = null,
        string? cookie = null,
        bool csrf = false) =>
        client.SendAsync(Request(method, path, bearer, cookie, csrf), TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> JsonAsync<T>(
        HttpClient client,
        HttpMethod method,
        string path,
        T body,
        string? bearer = null,
        string? cookie = null,
        bool csrf = false)
    {
        var request = Request(method, path, bearer, cookie, csrf);
        request.Content = JsonContent.Create(body);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static HttpRequestMessage Request(
        HttpMethod method,
        string path,
        string? bearer,
        string? cookie,
        bool csrf)
    {
        var request = new HttpRequestMessage(method, path);
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", BrowserCookie.PlainName + "=" + cookie);
        }

        if (csrf)
        {
            request.Headers.Add(CsrfProtection.Header, "1");
            request.Headers.Add("Origin", "http://localhost");
        }

        return request;
    }

    private static async Task<(HttpStatusCode Status, ProjectResponse Project)> ProjectAsync(
        HttpResponseMessage response)
    {
        using (response)
        {
            var project = await response.Content.ReadFromJsonAsync<ProjectResponse>(
                Json,
                TestContext.Current.CancellationToken);
            Assert.NotNull(project);
            return (response.StatusCode, project);
        }
    }

    private static async Task<ProjectResponse[]> ProjectsAsync(HttpResponseMessage response)
    {
        using (response)
        {
            return (await response.Content.ReadFromJsonAsync<ProjectResponse[]>(
                Json,
                TestContext.Current.CancellationToken))!;
        }
    }

    private static async Task<string?> ProblemCode(HttpResponseMessage response) =>
        (await Problem(response))?.Code;

    private static Task<ProblemResponse?> Problem(HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<ProblemResponse>(TestContext.Current.CancellationToken);
}
