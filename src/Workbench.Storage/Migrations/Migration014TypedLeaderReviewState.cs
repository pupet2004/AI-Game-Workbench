using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration014TypedLeaderReviewState
{
    public const long Version = 14;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(connection, transaction, "task_revisions", cancellationToken))
        {
            var revisionIndex = connection.CreateCommand();
            revisionIndex.Transaction = transaction;
            revisionIndex.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS ux_task_revisions_id_task_id_v14 ON task_revisions(id, task_id);";
            await revisionIndex.ExecuteNonQueryAsync(cancellationToken);
        }

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE UNIQUE INDEX IF NOT EXISTS ux_tasks_id_project_id
            ON tasks(id, project_id);

            CREATE TABLE IF NOT EXISTS task_review_decisions (
                review_decision_id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                task_id TEXT NOT NULL,
                revision_id TEXT NOT NULL,
                source_event_id TEXT NULL UNIQUE,
                outcome TEXT NOT NULL CHECK(outcome IN ('Pass', 'Fix', 'Continue', 'AskUser')),
                action_level TEXT NOT NULL CHECK(action_level IN ('L1LocalFix', 'L2TaskRework', 'L3DecisionRequired')),
                authority_mode TEXT NOT NULL CHECK(authority_mode IN ('Cautious', 'Balanced', 'Autonomous')),
                authority_resolution TEXT NOT NULL CHECK(authority_resolution IN ('AutoProceed', 'NotifyAndProceed', 'AskUser')),
                created_at TEXT NOT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE,
                FOREIGN KEY(task_id, project_id) REFERENCES tasks(id, project_id) ON DELETE CASCADE,
                FOREIGN KEY(revision_id, task_id) REFERENCES task_revisions(id, task_id) ON DELETE CASCADE
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_task_review_decisions_identity
            ON task_review_decisions(review_decision_id, project_id, task_id, revision_id);

            CREATE INDEX IF NOT EXISTS ix_task_review_decisions_project_task_created
            ON task_review_decisions(project_id, task_id, created_at, review_decision_id);

            CREATE TABLE IF NOT EXISTS task_review_user_gates (
                review_decision_id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                task_id TEXT NOT NULL,
                revision_id TEXT NOT NULL,
                question_message_id INTEGER NOT NULL,
                user_message_id INTEGER NULL,
                opened_at TEXT NOT NULL,
                responded_at TEXT NULL,
                state TEXT NOT NULL CHECK(state IN ('Open', 'Responded')),
                CHECK(
                    (state = 'Open' AND user_message_id IS NULL AND responded_at IS NULL) OR
                    (state = 'Responded' AND user_message_id IS NOT NULL AND responded_at IS NOT NULL)),
                FOREIGN KEY(review_decision_id, project_id, task_id, revision_id)
                    REFERENCES task_review_decisions(review_decision_id, project_id, task_id, revision_id)
                    ON DELETE CASCADE,
                FOREIGN KEY(question_message_id) REFERENCES leader_messages(id) ON DELETE RESTRICT,
                FOREIGN KEY(user_message_id) REFERENCES leader_messages(id) ON DELETE RESTRICT
            );

            CREATE INDEX IF NOT EXISTS ix_task_review_user_gates_project_state_opened
            ON task_review_user_gates(project_id, state, opened_at, review_decision_id);

            PRAGMA user_version = 14;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $tableName;";
        command.Parameters.AddWithValue("$tableName", tableName);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }
}
