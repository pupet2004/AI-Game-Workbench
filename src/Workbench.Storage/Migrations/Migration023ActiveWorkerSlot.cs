using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration023ActiveWorkerSlot
{
    public const long Version = 23;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DROP INDEX IF EXISTS ux_worker_active_project;
            CREATE UNIQUE INDEX ux_worker_active_project
                ON worker_executions(project_id)
                WHERE state IN ('Preparing','WorkspaceCreating','WorkspaceCreated','RuntimeStarting','Running','Blocked');
            PRAGMA user_version = 23;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
