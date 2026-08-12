using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration002PersistentLeaderSessions
{
    public const long Version = 2;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE project_leaders (
                project_id TEXT PRIMARY KEY,
                current_epoch_id TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE,
                FOREIGN KEY(current_epoch_id) REFERENCES leader_session_epochs(id) ON DELETE SET NULL
            );

            CREATE TABLE leader_session_epochs (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                provider_id TEXT NOT NULL,
                provider_account_id TEXT NOT NULL,
                model_id TEXT NOT NULL,
                agent_session_id TEXT NOT NULL,
                external_session_id TEXT NULL,
                working_directory TEXT NULL,
                started_at TEXT NOT NULL,
                last_active_at TEXT NOT NULL,
                ended_at TEXT NULL,
                rollover_reason TEXT NULL,
                handoff_summary TEXT NULL,
                FOREIGN KEY(project_id) REFERENCES project_leaders(project_id) ON DELETE CASCADE
            );

            CREATE INDEX ix_leader_session_epochs_project_id
            ON leader_session_epochs(project_id);

            CREATE UNIQUE INDEX ux_leader_session_epochs_active_project
            ON leader_session_epochs(project_id)
            WHERE ended_at IS NULL;

            CREATE TABLE leader_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                epoch_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                role TEXT NOT NULL CHECK(role IN ('user', 'assistant')),
                text TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY(epoch_id) REFERENCES leader_session_epochs(id) ON DELETE CASCADE,
                UNIQUE(epoch_id, sequence)
            );

            PRAGMA user_version = 2;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
