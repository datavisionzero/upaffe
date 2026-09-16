using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Upaffe.Infrastructure.Persistence;

internal static class PostgresRowLocks
{
    public static Task<bool> HttpCheckAsync(
        UpaffeDbContext context,
        Guid id,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken) =>
        ExistsAsync(context, "select id from http_check where id = @id for update", id, transaction, cancellationToken);

    public static Task<bool> HttpMonitorAsync(
        UpaffeDbContext context,
        Guid id,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken) =>
        ExistsAsync(context, "select id from http_monitor where id = @id for update", id, transaction, cancellationToken);

    private static async Task<bool> ExistsAsync(
        UpaffeDbContext context,
        string sql,
        Guid id,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = sql;
        command.Parameters.Add(new NpgsqlParameter<Guid>("id", id));
        return await command.ExecuteScalarAsync(cancellationToken) is Guid;
    }
}
