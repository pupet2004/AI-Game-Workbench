using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration004ProjectMemoryFoundation
{
    public const long Version = 4;
    public static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE project_activity_events (id TEXT PRIMARY KEY, project_id TEXT NOT NULL, event_type TEXT NOT NULL, summary TEXT NOT NULL, source_type TEXT NOT NULL, source_ref TEXT NULL, occurred_at TEXT NOT NULL, created_at TEXT NOT NULL, FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE);
            CREATE TABLE project_memory_items (id TEXT PRIMARY KEY, project_id TEXT NOT NULL, layer TEXT NOT NULL, topic TEXT NOT NULL, content TEXT NOT NULL, status TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL, certified_at TEXT NULL, FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE);
            CREATE TABLE project_memory_sources (memory_id TEXT NOT NULL, source_type TEXT NOT NULL, source_ref TEXT NOT NULL, PRIMARY KEY(memory_id, source_type, source_ref), FOREIGN KEY(memory_id) REFERENCES project_memory_items(id) ON DELETE CASCADE);
            CREATE INDEX ix_project_activity_events_project_id ON project_activity_events(project_id, occurred_at);
            CREATE INDEX ix_project_memory_items_project_id ON project_memory_items(project_id, layer, status);
            PRAGMA user_version = 4;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
