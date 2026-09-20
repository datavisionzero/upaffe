using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Upaffe.Application.Ports;
using Upaffe.Domain.Monitoring;
using Upaffe.Domain.Projects;
using Upaffe.Domain.Notifications;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class MonitorInventoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Fact]
    public async Task Inventory_filters_orders_and_pages_safe_HTTP_and_push_facts()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var instance = AnInstance.Against(connection, clock: new MutableTimeProvider(Now));
        var token = await instance.EstablishAsync("operator@example.test", "a long operator password");
        using var client = instance.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        await using (var context = AnInstance.ContextFor(connection))
        {
            var alpha = Project.Create("alpha", "Alpha project", Now.AddHours(-2), []);
            var beta = Project.Create("beta", "Beta project", Now.AddHours(-2), []);
            var failed = HttpMonitor.Create(alpha.Id, "site", "Public site",
                "https://status.example.test/health?access=hidden-query-value", 200,
                TextCondition.None, null, 60, 10, 1, null, null, Now.AddMinutes(-3),
                "Checks the public site.");
            var healthy = PushMonitor.Create(beta.Id, "backup", "Nightly backup",
                PushMonitorMode.JobCompletion, 3600, 300, null, null,
                Now.AddMinutes(-1), "Confirms backup completion.");
            context.AddRange(alpha, beta, failed, healthy);
            context.MaintenanceWindows.Add(MaintenanceWindow.Start("project", alpha.Id,
                1, Now.AddMinutes(-1), TimeSpan.FromMinutes(20)));
            context.HttpMonitorSecrets.Add(HttpMonitorSecret.FromTarget(failed.Id,
                "https://status.example.test/health?access=hidden-query-value"));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            var check = failed.BeginCheck(CheckTrigger.Requested, Now.AddMinutes(-2), Now.AddMinutes(-2));
            check.CompleteFailure("status_mismatch", Now.AddMinutes(-2), 503, 50,
                "https://status.example.test/health");
            Assert.True(failed.ApplyResult(check, Now.AddMinutes(-2)));
            context.HttpChecks.Add(check);
            var report = healthy.Receive(Guid.NewGuid(), Now.AddSeconds(-30),
                Now.AddSeconds(-30), ReportOutcome.Success, null);
            Assert.True(healthy.ApplyReport(report));
            context.PushReports.Add(report);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/monitors?limit=1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("hidden-query-value", body);
        Assert.DoesNotContain(token, body);
        var first = JsonSerializer.Deserialize<MonitorInventoryPage>(body, Json)!;
        Assert.Equal(2, first.Total);
        Assert.True(first.HasMore);
        Assert.Equal("site", Assert.Single(first.Items).Key);
        Assert.Equal("Checks the public site.", first.Items[0].Purpose);
        Assert.Equal("https://status.example.test/health", first.Items[0].TargetUrl);
        Assert.True(first.Items[0].Overdue);
        Assert.Equal(Now.AddMinutes(19), first.Items[0].MaintenanceUntil);

        var second = await GetAsync(client, token, "/api/monitors?limit=1&offset=1");
        Assert.Equal("backup", Assert.Single(second.Items).Key);
        Assert.Equal("job_completion", second.Items[0].Mode);
        Assert.NotNull(second.Items[0].LatestObservationAt);
        Assert.NotNull(second.Items[0].LastSuccessAt);
        Assert.False(second.HasMore);

        var searched = await GetAsync(client, token, "/api/monitors?project=beta&type=push&state=healthy&q=completion");
        Assert.Equal("backup", Assert.Single(searched.Items).Key);
        Assert.Equal(0, (await GetAsync(client, token, "/api/monitors?q=hidden-query-value")).Total);
        Assert.Equal(0, (await GetAsync(client, token, "/api/monitors?project=alpha&type=push")).Total);
        using var anonymous = await client.GetAsync("/api/monitors", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var counter = new QueryCounter();
        await using var counted = new UpaffeDbContext(new DbContextOptionsBuilder<UpaffeDbContext>()
            .UseNpgsql(connection).AddInterceptors(counter).Options);
        var page = await new MonitorInventoryStore(counted).ReadAsync(
            new(null, null, null, null, 1, 0), Now, TestContext.Current.CancellationToken);
        Assert.Equal(2, page.Total);
        Assert.Equal(2, counter.Count);
    }

    private static async Task<MonitorInventoryPage> GetAsync(HttpClient client, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MonitorInventoryPage>(Json,
            TestContext.Current.CancellationToken))!;
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
