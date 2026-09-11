using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration030CanonicalWorkerCompletions
{
    public const long Version = 30;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE canonical_worker_completions (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                source_event_id TEXT NOT NULL,
                task_id TEXT NOT NULL,
                task_revision_id TEXT NOT NULL,
                worker_execution_id TEXT NOT NULL,
                attempt_id TEXT NOT NULL,
                session_binding_id TEXT NOT NULL,
                worker_actor_id TEXT NOT NULL,
                final_report TEXT NOT NULL CHECK(length(trim(final_report)) > 0),
                validation_summary TEXT NULL,
                evidence_refs_json TEXT NOT NULL CHECK(json_valid(evidence_refs_json) AND json_type(evidence_refs_json) = 'array'),
                planned_result_claim_id TEXT NOT NULL,
                planned_validation_claim_id TEXT NULL,
                planned_handoff_id TEXT NOT NULL,
                result_claim_id TEXT NULL,
                validation_claim_id TEXT NULL,
                handoff_id TEXT NULL,
                status TEXT NOT NULL CHECK(status IN ('PendingBridge', 'GovernanceReady', 'Governed')),
                created_at TEXT NOT NULL,
                governed_at TEXT NULL,
                authority_decision_id TEXT NULL,
                UNIQUE(project_id, source_event_id),
                UNIQUE(project_id, worker_execution_id),
                FOREIGN KEY(task_id) REFERENCES tasks(id) ON DELETE CASCADE,
                FOREIGN KEY(task_revision_id) REFERENCES task_revisions(id) ON DELETE CASCADE,
                FOREIGN KEY(worker_execution_id) REFERENCES worker_executions(id) ON DELETE CASCADE,
                FOREIGN KEY(attempt_id, project_id) REFERENCES b1_attempts(id, project_id),
                FOREIGN KEY(session_binding_id, project_id) REFERENCES b1_session_bindings(id, project_id),
                FOREIGN KEY(worker_actor_id, project_id) REFERENCES b1_logical_actors(id, project_id),
                FOREIGN KEY(result_claim_id, project_id) REFERENCES b1_claims(id, project_id),
                FOREIGN KEY(validation_claim_id, project_id) REFERENCES b1_claims(id, project_id),
                FOREIGN KEY(handoff_id, project_id) REFERENCES b1_handoffs(id, project_id),
                FOREIGN KEY(authority_decision_id, project_id) REFERENCES b1_authority_decisions(id, project_id)
            );
            CREATE UNIQUE INDEX ux_canonical_completion_claim_result
                ON canonical_worker_completions(project_id, result_claim_id);
            CREATE UNIQUE INDEX ux_canonical_completion_planned_claim_result
                ON canonical_worker_completions(project_id, planned_result_claim_id);
            CREATE UNIQUE INDEX ux_canonical_completion_claim_validation
                ON canonical_worker_completions(project_id, validation_claim_id)
                WHERE validation_claim_id IS NOT NULL;
            CREATE UNIQUE INDEX ux_canonical_completion_planned_claim_validation
                ON canonical_worker_completions(project_id, planned_validation_claim_id)
                WHERE planned_validation_claim_id IS NOT NULL;
            CREATE UNIQUE INDEX ux_canonical_completion_handoff
                ON canonical_worker_completions(project_id, handoff_id);
            CREATE UNIQUE INDEX ux_canonical_completion_planned_handoff
                ON canonical_worker_completions(project_id, planned_handoff_id);
            CREATE INDEX ix_canonical_worker_completions_project_status
                ON canonical_worker_completions(project_id, status, created_at);
            PRAGMA user_version = 30;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
