using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration028ProjectEvolutionCandidates
{
    public const long Version = 28;

    public static async Task ApplyAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE project_evolution_candidates(
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                epoch_id TEXT NULL REFERENCES leader_session_epochs(id) ON DELETE SET NULL,
                result_id TEXT NULL,
                source_ref TEXT NOT NULL,
                object_name TEXT NOT NULL,
                object_kind TEXT NOT NULL,
                change_type TEXT NOT NULL,
                before_text TEXT NULL,
                after_text TEXT NULL,
                impact_class TEXT NOT NULL,
                route_hint TEXT NOT NULL,
                reason TEXT NOT NULL,
                status TEXT NOT NULL CHECK(status IN ('Observed','GovernancePending','Accepted','Rejected')),
                created_at TEXT NOT NULL);
            CREATE INDEX ix_project_evolution_candidates_project_time
                ON project_evolution_candidates(project_id, created_at DESC, id DESC);
            PRAGMA user_version = 28;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
