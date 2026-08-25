using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration022EvidenceRecords
{
    public const long Version = 22;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE b1_evidence_records (
                project_id TEXT NOT NULL,
                evidence_ref TEXT NOT NULL,
                kind TEXT NOT NULL,
                locator TEXT NOT NULL,
                digest_algorithm TEXT NULL,
                digest_hex TEXT NULL,
                content_length INTEGER NULL CHECK(content_length IS NULL OR content_length >= 0),
                created_at TEXT NOT NULL,
                PRIMARY KEY(project_id, evidence_ref),
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE);

            CREATE INDEX ix_b1_evidence_records_project_created
                ON b1_evidence_records(project_id, created_at DESC, evidence_ref);

            PRAGMA user_version = 22;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
