using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration012LeaderReviewState
{
    public const long Version = 12;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE tasks_rebuilt (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                title TEXT NOT NULL,
                status TEXT NOT NULL CHECK(status IN (
                    'Draft','ReadyToStart','Working','NeedsLeaderDecision','Reviewing',
                    'NeedsUserDecision','Completed','Cancelled')),
                current_revision_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                cancelled_at TEXT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE);
            INSERT INTO tasks_rebuilt (
                id,project_id,title,status,current_revision_id,created_at,updated_at,cancelled_at)
            SELECT id,project_id,title,status,current_revision_id,created_at,updated_at,cancelled_at
            FROM tasks;
            DROP TABLE tasks;
            ALTER TABLE tasks_rebuilt RENAME TO tasks;
            CREATE INDEX ix_tasks_project_status ON tasks(project_id,status);
            CREATE INDEX ix_tasks_project_created ON tasks(project_id,created_at);
            PRAGMA user_version = 12;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
