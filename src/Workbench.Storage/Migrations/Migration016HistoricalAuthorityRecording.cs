using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration016HistoricalAuthorityRecording
{
    public const long Version = 16;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE task_review_decisions_rebuilt (
                review_decision_id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                task_id TEXT NOT NULL,
                revision_id TEXT NOT NULL,
                source_event_id TEXT NULL UNIQUE,
                outcome TEXT NOT NULL CHECK(outcome IN ('Pass', 'Fix', 'Continue', 'AskUser')),
                action_level TEXT NOT NULL CHECK(action_level IN ('L1LocalFix', 'L2TaskRework', 'L3DecisionRequired')),
                authority_mode TEXT NULL CHECK(authority_mode IN ('Cautious', 'Balanced', 'Autonomous')),
                authority_resolution TEXT NOT NULL CHECK(authority_resolution IN ('AutoProceed', 'NotifyAndProceed', 'AskUser')),
                authority_mode_recording TEXT NOT NULL CHECK(authority_mode_recording IN ('Recorded', 'LegacyNotRecorded')),
                created_at TEXT NOT NULL,
                CHECK(
                    (authority_mode_recording = 'Recorded' AND authority_mode IS NOT NULL) OR
                    (authority_mode_recording = 'LegacyNotRecorded' AND authority_mode IS NULL)
                ),
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE,
                FOREIGN KEY(task_id, project_id) REFERENCES tasks(id, project_id) ON DELETE CASCADE,
                FOREIGN KEY(revision_id, task_id) REFERENCES task_revisions(id, task_id) ON DELETE CASCADE
            );

            INSERT INTO task_review_decisions_rebuilt (
                review_decision_id,project_id,task_id,revision_id,source_event_id,outcome,
                action_level,authority_mode,authority_resolution,authority_mode_recording,created_at)
            SELECT review_decision_id,project_id,task_id,revision_id,source_event_id,outcome,
                action_level,authority_mode,authority_resolution,'Recorded',created_at
            FROM task_review_decisions;

            DROP TABLE task_review_decisions;
            ALTER TABLE task_review_decisions_rebuilt RENAME TO task_review_decisions;

            CREATE UNIQUE INDEX ux_task_review_decisions_identity
            ON task_review_decisions(review_decision_id, project_id, task_id, revision_id);

            CREATE INDEX ix_task_review_decisions_project_task_created
            ON task_review_decisions(project_id, task_id, created_at, review_decision_id);

            PRAGMA user_version = 16;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
