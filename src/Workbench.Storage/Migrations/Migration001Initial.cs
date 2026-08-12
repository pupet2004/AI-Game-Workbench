using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration001Initial
{
    public const long Version = 1;

    public static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE projects (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                root_path TEXT NOT NULL,
                project_type INTEGER NOT NULL,
                git_root TEXT NULL,
                created_at TEXT NOT NULL,
                last_opened_at TEXT NOT NULL
            );

            CREATE UNIQUE INDEX ix_projects_root_path
            ON projects(root_path COLLATE NOCASE);

            CREATE TABLE project_layouts (
                project_id TEXT PRIMARY KEY,
                leader_width REAL NOT NULL,
                work_width REAL NOT NULL,
                library_width REAL NOT NULL,
                focused_pane INTEGER NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        var versionCommand = connection.CreateCommand();
        versionCommand.Transaction = transaction;
        versionCommand.CommandText = "PRAGMA user_version = 1;";
        await versionCommand.ExecuteNonQueryAsync(cancellationToken);
    }
}
