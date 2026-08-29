using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration024ConcurrentWorkerSessions
{
    public const long Version = 24;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DROP INDEX IF EXISTS ux_worker_active_project;
            PRAGMA user_version = 24;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
