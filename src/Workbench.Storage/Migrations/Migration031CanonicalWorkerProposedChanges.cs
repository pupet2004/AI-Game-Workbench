using Microsoft.Data.Sqlite;

namespace Workbench.Storage.Migrations;

internal static class Migration031CanonicalWorkerProposedChanges
{
    public const long Version = 31;

    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ALTER TABLE canonical_worker_completions
                ADD COLUMN proposed_changes_json TEXT NOT NULL DEFAULT '[]'
                CHECK(json_valid(proposed_changes_json) AND json_type(proposed_changes_json) = 'array');
            PRAGMA user_version = 31;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
