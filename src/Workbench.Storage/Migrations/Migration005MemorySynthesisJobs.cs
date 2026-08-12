using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration005MemorySynthesisJobs
{
    public const long Version = 5;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE project_memory_synthesis_jobs (
                epoch_id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                status TEXT NOT NULL CHECK(status IN ('Pending', 'Running', 'Completed')),
                attempt_count INTEGER NOT NULL,
                last_attempted_at TEXT NULL,
                completed_at TEXT NULL,
                last_error TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY(epoch_id) REFERENCES leader_session_epochs(id) ON DELETE CASCADE,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE);
            CREATE INDEX ix_project_memory_synthesis_jobs_project_status
                ON project_memory_synthesis_jobs(project_id, status, created_at, epoch_id);
            PRAGMA user_version = 5;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
