using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration026WorkerWorkspaceBaseline
{
    public const long Version = 26;

    public static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "ALTER TABLE worker_executions ADD COLUMN workspace_baseline_json TEXT NULL; PRAGMA user_version = 26;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
