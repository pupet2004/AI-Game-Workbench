using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration015ReviewGateReferenceDecoupling
{
    public const long Version = 15;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE task_review_user_gates_rebuilt (
                review_decision_id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                task_id TEXT NOT NULL,
                revision_id TEXT NOT NULL,
                question_message_id INTEGER NULL,
                user_message_id INTEGER NULL,
                opened_at TEXT NOT NULL,
                responded_at TEXT NULL,
                state TEXT NOT NULL CHECK(state IN ('Open', 'Responded')),
                CHECK(
                    (state = 'Open' AND user_message_id IS NULL AND responded_at IS NULL) OR
                    (state = 'Responded' AND responded_at IS NOT NULL)),
                FOREIGN KEY(review_decision_id, project_id, task_id, revision_id)
                    REFERENCES task_review_decisions(review_decision_id, project_id, task_id, revision_id)
                    ON DELETE CASCADE,
                FOREIGN KEY(question_message_id) REFERENCES leader_messages(id) ON DELETE SET NULL,
                FOREIGN KEY(user_message_id) REFERENCES leader_messages(id) ON DELETE SET NULL
            );

            INSERT INTO task_review_user_gates_rebuilt (
                review_decision_id,project_id,task_id,revision_id,question_message_id,
                user_message_id,opened_at,responded_at,state)
            SELECT review_decision_id,project_id,task_id,revision_id,question_message_id,
                user_message_id,opened_at,responded_at,state
            FROM task_review_user_gates;

            DROP TABLE task_review_user_gates;
            ALTER TABLE task_review_user_gates_rebuilt RENAME TO task_review_user_gates;

            CREATE INDEX ix_task_review_user_gates_project_state_opened
            ON task_review_user_gates(project_id, state, opened_at, review_decision_id);

            PRAGMA user_version = 15;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
