using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration008ProjectLibrary
{
    public const long Version = 8;
    public static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "CREATE TABLE IF NOT EXISTS project_library_entries (id TEXT PRIMARY KEY, project_id TEXT NOT NULL, source_session_id TEXT NOT NULL, task_id TEXT NULL, category TEXT NOT NULL, topic TEXT NOT NULL, summary TEXT NOT NULL, source_reference TEXT NULL, created_at TEXT NOT NULL, FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE, FOREIGN KEY(task_id) REFERENCES tasks(id) ON DELETE SET NULL); CREATE INDEX IF NOT EXISTS ix_project_library_project_time ON project_library_entries(project_id, created_at DESC); PRAGMA user_version = 8;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
