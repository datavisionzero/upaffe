using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Upaffe.Infrastructure.Persistence;

/// <summary>Serializes and applies forward-only migrations during startup.</summary>
public sealed class SchemaMigrator(UpaffeDbContext context, ILogger<SchemaMigrator> logger)
{
    private const long AdvisoryLockKey = 0x_0A_AFFE;

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await context.Database.ExecuteSqlRawAsync(
                "select pg_advisory_lock({0})", [AdvisoryLockKey], cancellationToken);

            var unknown = SchemaVersions.NotKnownHere(
                await context.Database.GetAppliedMigrationsAsync(cancellationToken),
                context.Database.GetMigrations());
            if (unknown.Count > 0)
            {
                throw new SchemaIsNewerException(unknown);
            }

            var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            if (pending.Length == 0)
            {
                logger.LogInformation("Schema is current; nothing to migrate.");
                return;
            }

            logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Length, pending);
            await context.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Schema is current.");
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync(
                "select pg_advisory_unlock({0})", [AdvisoryLockKey], CancellationToken.None);
            await context.Database.CloseConnectionAsync();
        }
    }

    public async Task<bool> AppliedAsync(CancellationToken cancellationToken)
    {
        var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
        var known = context.Database.GetMigrations().ToArray();
        return SchemaVersions.NotKnownHere(applied, known).Count == 0
            && !known.Except(applied, StringComparer.Ordinal).Any();
    }
}
