using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration021LibraryProjectionProvenance
{
    public const long Version = 21;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE project_library_proposals_v21 (
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                source_session_id TEXT NULL,
                status TEXT NOT NULL CHECK(status IN ('Pending','Accepted','Rejected')),
                payload_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                decided_at TEXT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE);

            INSERT INTO project_library_proposals_v21(
                id,project_id,source_session_id,status,payload_json,created_at,decided_at)
            SELECT id,project_id,source_session_id,status,payload_json,created_at,decided_at
            FROM project_library_proposals;

            DROP TABLE project_library_proposals;
            ALTER TABLE project_library_proposals_v21 RENAME TO project_library_proposals;

            CREATE INDEX ix_library_proposals_project_status
                ON project_library_proposals(project_id, status, created_at DESC, id);

            PRAGMA user_version = 21;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
