using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration003LeaderRotationSettings
{
    public const long Version = 3;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE workbench_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE project_settings (
                project_id TEXT PRIMARY KEY,
                leader_session_rotation_policy TEXT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE
            );

            PRAGMA user_version = 3;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
