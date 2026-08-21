using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration019ProjectSummary
{
    public const long Version = 19;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE leader_messages ADD COLUMN result_id TEXT NULL;
            ALTER TABLE leader_messages ADD COLUMN summary_delta_payload_json TEXT NULL
                CHECK(
                    (summary_delta_payload_json IS NULL AND result_id IS NULL) OR
                    (summary_delta_payload_json IS NOT NULL AND result_id IS NOT NULL AND role = 'assistant'));
            ALTER TABLE leader_messages ADD COLUMN summary_persisted_at TEXT NULL
                CHECK(
                    summary_persisted_at IS NULL OR
                    (result_id IS NOT NULL AND summary_delta_payload_json IS NOT NULL AND role = 'assistant'));

            CREATE INDEX ix_leader_messages_pending_summary
            ON leader_messages(summary_persisted_at, result_id)
            WHERE result_id IS NOT NULL AND summary_delta_payload_json IS NOT NULL;

            CREATE TABLE project_summary_entries (
                entry_id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                occurred_at TEXT NOT NULL,
                created_at TEXT NOT NULL,
                kind TEXT NOT NULL CHECK(kind IN ('Decision', 'Change', 'Constraint', 'RejectedPath', 'Unresolved')),
                text TEXT NOT NULL CHECK(length(trim(text)) > 0),
                result_id TEXT NOT NULL,
                delta_ordinal INTEGER NOT NULL CHECK(delta_ordinal >= 0)
            );

            CREATE UNIQUE INDEX ux_project_summary_entries_result_ordinal
            ON project_summary_entries(result_id, delta_ordinal);

            CREATE INDEX ix_project_summary_entries_project_order
            ON project_summary_entries(project_id, occurred_at DESC, created_at DESC, entry_id);

            CREATE TABLE project_summary_source_refs (
                entry_id TEXT NOT NULL REFERENCES project_summary_entries(entry_id) ON DELETE CASCADE,
                ordinal INTEGER NOT NULL CHECK(ordinal >= 0),
                source_kind TEXT NOT NULL CHECK(length(trim(source_kind)) > 0),
                source_locator TEXT NOT NULL CHECK(length(trim(source_locator)) > 0),
                PRIMARY KEY(entry_id, ordinal)
            );

            CREATE INDEX ix_project_summary_source_refs_filter
            ON project_summary_source_refs(source_kind, source_locator, entry_id);

            PRAGMA user_version = 19;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
