using Microsoft.EntityFrameworkCore;
using Npgsql;
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
