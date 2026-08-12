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

        if (currentVersion >= Migration001Initial.Version)
        {
            return;
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await Migration001Initial.ApplyAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
