using Microsoft.EntityFrameworkCore;
using Upaffe.Application.Ports;
using Upaffe.Domain.Notifications;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class EmailDeliveryPersistenceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Concurrent_workers_claim_once_and_a_restart_recovers_an_expired_lease()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = await postgres.CreateDatabaseAsync();
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        await using (var setup = AnInstance.ContextFor(connection))
        {
            await AnInstance.MigratorFor(setup).ApplyAsync(ct);
            setup.NotificationDeliveries.Add(New(now));
            await setup.SaveChangesAsync(ct);
        }

        await using var firstContext = AnInstance.ContextFor(connection);
        await using var secondContext = AnInstance.ContextFor(connection);
        var firstStore = new EmailDeliveryStore(firstContext);
        var secondStore = new EmailDeliveryStore(secondContext);
        var claims = await Task.WhenAll(
            firstStore.ClaimAsync(now, TimeSpan.FromMinutes(2), ct),
            secondStore.ClaimAsync(now, TimeSpan.FromMinutes(2), ct));
        var first = Assert.Single(claims.OfType<EmailDeliveryLease>());
        Assert.Single(claims, value => value is null);
        await BeginAsync(firstContext, first, now, ct);

        await using var restartedContext = AnInstance.ContextFor(connection);
        var restartedStore = new EmailDeliveryStore(restartedContext);
        Assert.Null(await restartedStore.ClaimAsync(now.AddMinutes(1), TimeSpan.FromMinutes(2), ct));
        var recovered = await restartedStore.ClaimAsync(now.AddMinutes(3), TimeSpan.FromMinutes(2), ct);
        Assert.NotNull(recovered);
        Assert.Equal(first.DeliveryId, recovered.DeliveryId);
        Assert.NotEqual(first.Token, recovered.Token);
        await BeginAsync(restartedContext, recovered, now.AddMinutes(3), ct);
        Assert.False(await firstStore.CompleteAsync(first.DeliveryId, first.Token,
            EmailSendResult.Accepted(), now.AddMinutes(3), ct));
        Assert.True(await restartedStore.CompleteAsync(recovered.DeliveryId, recovered.Token,
            EmailSendResult.Accepted(), now.AddMinutes(3), ct));

        await using var inspect = AnInstance.ContextFor(connection);
        var persisted = await inspect.NotificationDeliveries.SingleAsync(ct);
        Assert.Equal(DeliveryState.Accepted, persisted.State);
        Assert.Equal(2, persisted.AttemptCount);
        Assert.Equal(now.AddMinutes(3), persisted.AcceptedAt);
        Assert.Null(await new EmailDeliveryStore(inspect).ClaimAsync(now.AddDays(1),
            TimeSpan.FromMinutes(2), ct));
    }

    [Fact]
    public async Task Retry_and_terminal_failure_survive_new_store_instances()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = await postgres.CreateDatabaseAsync();
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        await using (var setup = AnInstance.ContextFor(connection))
        {
            await AnInstance.MigratorFor(setup).ApplyAsync(ct);
            setup.NotificationDeliveries.Add(New(now));
            await setup.SaveChangesAsync(ct);
        }

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await using var context = AnInstance.ContextFor(connection);
            var store = new EmailDeliveryStore(context);
            var lease = await store.ClaimAsync(now, TimeSpan.FromMinutes(2), ct);
            Assert.NotNull(lease);
            await BeginAsync(context, lease, now, ct);
            Assert.True(await store.CompleteAsync(lease.DeliveryId, lease.Token,
                EmailSendResult.Failed("smtp_timeout", true), now, ct));
            if (attempt < 5)
            {
                Assert.Null(await store.ClaimAsync(now, TimeSpan.FromMinutes(2), ct));
                now = now.AddMinutes(1 << (attempt - 1));
            }
        }
        await using var inspect = AnInstance.ContextFor(connection);
        var persisted = await inspect.NotificationDeliveries.SingleAsync(ct);
        Assert.Equal(DeliveryState.TerminalFailure, persisted.State);
        Assert.Equal(5, persisted.AttemptCount);
        Assert.Equal("smtp_timeout", persisted.LastErrorCode);
        Assert.Null(await new EmailDeliveryStore(inspect).ClaimAsync(now.AddDays(1),
            TimeSpan.FromMinutes(2), ct));
    }

    private static NotificationDelivery New(DateTimeOffset now) =>
        NotificationDelivery.Queue(Guid.NewGuid(), NotificationKind.Alert,
            "ops@example.test", "project", "Project", "monitor", "Monitor",
            "http", "timeout", now, now);

    private static async Task BeginAsync(UpaffeDbContext context,
        EmailDeliveryLease lease, DateTimeOffset now, CancellationToken ct)
    {
        var delivery = await context.NotificationDeliveries.SingleAsync(
            value => value.Id == lease.DeliveryId, ct);
        delivery.BeginAttempt(lease.Token, now);
        await context.SaveChangesAsync(ct);
    }
}
