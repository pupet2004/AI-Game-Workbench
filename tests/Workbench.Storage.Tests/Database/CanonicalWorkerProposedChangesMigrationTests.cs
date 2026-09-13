using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Tests.Database;

public sealed class CanonicalWorkerProposedChangesMigrationTests
{
    [Fact]
    public async Task Migration031_adds_proposed_changes_json_with_array_default_and_constraint()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);

        await database.InitializeAsync();
        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();

        Assert.Equal(31L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));
        Assert.Equal(
            "proposed_changes_json|TEXT|1|0|'[]'",
            await ScalarAsync<string>(
                connection,
                "SELECT name || '|' || type || '|' || \"notnull\" || '|' || pk || '|' || ifnull(dflt_value, 'NULL') FROM pragma_table_info('canonical_worker_completions') WHERE name='proposed_changes_json';"));

        var tableSql = await ScalarAsync<string>(
            connection,
            "SELECT sql FROM sqlite_master WHERE type='table' AND name='canonical_worker_completions';");
        Assert.Contains("json_valid(proposed_changes_json)", tableSql, StringComparison.Ordinal);
        Assert.Contains("json_type(proposed_changes_json) = 'array'", tableSql, StringComparison.Ordinal);

        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = OFF;";
        await pragma.ExecuteNonQueryAsync();

        await ExecuteAsync(connection, """
            INSERT INTO canonical_worker_completions(
                id,project_id,source_event_id,task_id,task_revision_id,worker_execution_id,
                attempt_id,session_binding_id,worker_actor_id,final_report,validation_summary,
                evidence_refs_json,planned_result_claim_id,planned_validation_claim_id,planned_handoff_id,
                result_claim_id,validation_claim_id,handoff_id,status,created_at)
            VALUES('completion','project','source','task','revision','execution','attempt','binding','actor',
                'report',NULL,'[]','result',NULL,'handoff',NULL,NULL,NULL,'PendingBridge','2026-01-01T00:00:00Z');
            """);

        Assert.Equal(
            "[]",
            await ScalarAsync<string>(
                connection,
                "SELECT proposed_changes_json FROM canonical_worker_completions WHERE id='completion';"));

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            connection,
            "UPDATE canonical_worker_completions SET proposed_changes_json='{}' WHERE id='completion';"));
    }

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Expected scalar result."));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
