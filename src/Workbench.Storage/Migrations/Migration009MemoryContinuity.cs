using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration009MemoryContinuity
{
    public const long Version = 9;

    public static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS project_daily_summaries (
                project_id TEXT NOT NULL,
                local_date TEXT NOT NULL,
                content TEXT NOT NULL,
                revision INTEGER NOT NULL CHECK(revision >= 1),
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY(project_id, local_date),
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS project_daily_summary_sources (
                project_id TEXT NOT NULL,
                local_date TEXT NOT NULL,
                source_type TEXT NOT NULL,
                source_ref TEXT NOT NULL,
                PRIMARY KEY(project_id, local_date, source_type, source_ref),
                FOREIGN KEY(project_id, local_date)
                    REFERENCES project_daily_summaries(project_id, local_date) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS project_memory_preferences (
                project_id TEXT PRIMARY KEY,
                library_granularity TEXT NOT NULL,
                continuity_mode TEXT NOT NULL,
                time_zone_id TEXT NOT NULL,
                custom_instructions TEXT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE);
            CREATE INDEX IF NOT EXISTS ix_project_daily_summaries_project_date
                ON project_daily_summaries(project_id, local_date DESC);
            PRAGMA user_version = 9;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
