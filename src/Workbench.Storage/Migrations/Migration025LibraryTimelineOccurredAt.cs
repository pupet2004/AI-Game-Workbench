using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration025LibraryTimelineOccurredAt
{
    public const long Version = 25;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE project_library_timeline_nodes
                ADD COLUMN occurred_at TEXT NOT NULL DEFAULT '';
            UPDATE project_library_timeline_nodes
            SET occurred_at = created_at
            WHERE occurred_at = '';
            CREATE INDEX IF NOT EXISTS ix_library_nodes_object_occurred
                ON project_library_timeline_nodes(object_id, occurred_at, id);
            PRAGMA user_version = 25;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
