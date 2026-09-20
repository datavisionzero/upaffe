using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using Npgsql;
using Upaffe.Api.Http;
using Upaffe.Domain.Access;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public sealed class MigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task An_empty_database_is_migrated_to_the_known_schema()
    {
        await using var context = AnInstance.ContextFor(await postgres.CreateDatabaseAsync());
        Assert.Empty(await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));

        await AnInstance.MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            context.Database.GetMigrations(),
            await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_second_start_finds_nothing_to_do()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var first = AnInstance.ContextFor(connectionString))
        {
            await AnInstance.MigratorFor(first).ApplyAsync(TestContext.Current.CancellationToken);
        }

        await using var second = AnInstance.ContextFor(connectionString);
        await AnInstance.MigratorFor(second).ApplyAsync(TestContext.Current.CancellationToken);
        Assert.Empty(await second.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_initialized_v0_1_database_starts_without_reopening_bootstrap()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using (var old = AnInstance.ContextFor(connection))
        {
            await old.Database.MigrateAsync(
                "20260919164529_RecordHttpCheckApplicability", TestContext.Current.CancellationToken);
            var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
            var person = Operator.Establish("operator@example.test", "$argon2id$existing-hash", now);
            var credential = ManagementCredential.Create(person.Id, "existing agent", now);
            var secret = ManagementCredentialSecret.Issue(credential.Id, now).Secret;
            old.AddRange(person, credential, secret);
            await old.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var instance = AnInstance.Against(connection);
        using var client = instance.CreateClient();
        Assert.Equal(new BootstrapStateResponse(false),
            await client.GetFromJsonAsync<BootstrapStateResponse>(
                "/api/bootstrap", TestContext.Current.CancellationToken));
        using var ready = await client.GetAsync("/api/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        await using var current = AnInstance.ContextFor(connection);
        Assert.Equal(1, await current.Operators.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await current.ManagementCredentials.CountAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await current.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Concurrent_starts_are_serialized()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var one = AnInstance.ContextFor(connectionString);
        await using var two = AnInstance.ContextFor(connectionString);

        await Task.WhenAll(
            AnInstance.MigratorFor(one).ApplyAsync(TestContext.Current.CancellationToken),
            AnInstance.MigratorFor(two).ApplyAsync(TestContext.Current.CancellationToken));

        Assert.Equal(
            one.Database.GetMigrations(),
            await one.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_schema_with_an_unknown_migration_is_refused()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var migrated = AnInstance.ContextFor(connectionString))
        {
            await AnInstance.MigratorFor(migrated).ApplyAsync(TestContext.Current.CancellationToken);
        }

        await AddUnknownMigrationAsync(connectionString);
        await using var context = AnInstance.ContextFor(connectionString);

        var refusal = await Assert.ThrowsAsync<SchemaIsNewerException>(
            () => AnInstance.MigratorFor(context).ApplyAsync(TestContext.Current.CancellationToken));
        Assert.Contains("SomethingThisBuildNeverHeardOf", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("no downgrade path", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_host_does_not_start_with_an_unknown_schema()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var migrated = AnInstance.ContextFor(connectionString))
        {
            await AnInstance.MigratorFor(migrated).ApplyAsync(TestContext.Current.CancellationToken);
        }

        await AddUnknownMigrationAsync(connectionString);
        await using var instance = AnInstance.Against(connectionString);

        await Assert.ThrowsAsync<SchemaIsNewerException>(() => Task.Run(
            () => instance.CreateClient(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_host_does_not_start_when_postgres_is_unreachable()
    {
        await using var instance = AnInstance.Against(
            postgres.ConnectionStringFor("a_database_nobody_created"));

        var failure = await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(
            () => instance.CreateClient(), TestContext.Current.CancellationToken));
        Assert.IsType<PostgresException>(failure, exactMatch: false);
    }

    private static async Task AddUnknownMigrationAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            insert into "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            values ('29990101000000_SomethingThisBuildNeverHeardOf', '10.0.11')
            """,
            connection);
        await command.ExecuteNonQueryAsync();
    }
}
