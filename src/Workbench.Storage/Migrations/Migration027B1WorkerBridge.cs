using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration027B1WorkerBridge
{
    public const long Version = 27;

    public static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE b1_worker_task_links (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                assignment_id TEXT NOT NULL,
                assignment_revision_id TEXT NOT NULL,
                worker_task_id TEXT NOT NULL,
                worker_task_revision_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                UNIQUE(project_id, worker_task_id),
                UNIQUE(project_id, assignment_id, worker_task_id),
                FOREIGN KEY(assignment_id, project_id) REFERENCES b1_assignments(id, project_id),
                FOREIGN KEY(assignment_revision_id, project_id, assignment_id) REFERENCES b1_revisions(id, project_id, assignment_id),
                FOREIGN KEY(worker_task_id) REFERENCES tasks(id) ON DELETE CASCADE,
                FOREIGN KEY(worker_task_revision_id) REFERENCES task_revisions(id) ON DELETE CASCADE
            );
            CREATE INDEX ix_b1_worker_task_links_assignment ON b1_worker_task_links(project_id, assignment_id);

            CREATE TABLE b1_worker_execution_links (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                attempt_id TEXT NOT NULL,
                worker_execution_id TEXT NOT NULL,
                relation_kind TEXT NOT NULL CHECK(relation_kind IN ('Initial','Retry','ProviderReplacement','Continuation')),
                created_at TEXT NOT NULL,
                UNIQUE(project_id, worker_execution_id),
                UNIQUE(project_id, attempt_id, worker_execution_id),
                FOREIGN KEY(attempt_id, project_id) REFERENCES b1_attempts(id, project_id),
                FOREIGN KEY(worker_execution_id) REFERENCES worker_executions(id) ON DELETE CASCADE
            );
            CREATE INDEX ix_b1_worker_execution_links_attempt ON b1_worker_execution_links(project_id, attempt_id, created_at);

            CREATE TABLE b1_worker_session_links (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                session_binding_id TEXT NOT NULL,
                worker_execution_id TEXT NOT NULL,
                agent_session_id TEXT NOT NULL,
                external_session_ref TEXT NOT NULL,
                provider_id TEXT NOT NULL,
                account_id TEXT NOT NULL,
                model_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                UNIQUE(project_id, worker_execution_id),
                FOREIGN KEY(session_binding_id, project_id) REFERENCES b1_session_bindings(id, project_id),
                FOREIGN KEY(worker_execution_id) REFERENCES worker_executions(id) ON DELETE CASCADE
            );

            CREATE TABLE b1_worker_execution_evidence (
                project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                evidence_ref TEXT NOT NULL,
                worker_execution_id TEXT NOT NULL,
                worker_task_id TEXT NOT NULL,
                worker_task_revision_id TEXT NOT NULL,
                attempt_id TEXT NOT NULL,
                verification_result TEXT NOT NULL CHECK(verification_result IN ('Passed','Failed','NotVerifiable')),
                verification_json TEXT NOT NULL CHECK(json_valid(verification_json)),
                created_at TEXT NOT NULL,
                PRIMARY KEY(project_id, evidence_ref),
                UNIQUE(project_id, worker_execution_id),
                FOREIGN KEY(worker_execution_id) REFERENCES worker_executions(id) ON DELETE CASCADE,
                FOREIGN KEY(worker_task_id) REFERENCES tasks(id) ON DELETE CASCADE,
                FOREIGN KEY(worker_task_revision_id) REFERENCES task_revisions(id) ON DELETE CASCADE,
                FOREIGN KEY(attempt_id, project_id) REFERENCES b1_attempts(id, project_id)
            );
            PRAGMA user_version = 27;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
