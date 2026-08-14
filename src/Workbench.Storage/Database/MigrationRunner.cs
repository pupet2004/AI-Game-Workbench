using Microsoft.Data.Sqlite;
using Workbench.Storage.Migrations;

namespace Workbench.Storage.Database;

internal static class MigrationRunner
{
    public static async Task RunAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "PRAGMA user_version;";
        var currentVersion = Convert.ToInt64(await versionCommand.ExecuteScalarAsync(cancellationToken));

        if (currentVersion < Migration001Initial.Version)
        {
            await ApplyAsync(
                connection,
                Migration001Initial.ApplyAsync,
                cancellationToken);
        }

        if (currentVersion < Migration002PersistentLeaderSessions.Version)
        {
            await ApplyAsync(
                connection,
                Migration002PersistentLeaderSessions.ApplyAsync,
                cancellationToken);
        }

        if (currentVersion < Migration003LeaderRotationSettings.Version)
        {
            await ApplyAsync(
                connection,
                Migration003LeaderRotationSettings.ApplyAsync,
                cancellationToken);
        }
        if (currentVersion < Migration004ProjectMemoryFoundation.Version)
        {
            await ApplyAsync(connection, Migration004ProjectMemoryFoundation.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration005MemorySynthesisJobs.Version)
        {
            await ApplyAsync(connection, Migration005MemorySynthesisJobs.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration006LeaderBootDelivery.Version)
        {
            await ApplyAsync(connection, Migration006LeaderBootDelivery.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration007LeaderWorkerDelegation.Version)
        {
            await ApplyAsync(connection, Migration007LeaderWorkerDelegation.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration008ProjectLibrary.Version)
        {
            await ApplyAsync(connection, Migration008ProjectLibrary.ApplyAsync, cancellationToken);
        }
        if (currentVersion < Migration009MemoryContinuity.Version)
        {
            await ApplyAsync(connection, Migration009MemoryContinuity.ApplyAsync, cancellationToken);
        }
    }

    private static async Task ApplyAsync(
        SqliteConnection connection,
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task> migration,
        CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await migration(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
