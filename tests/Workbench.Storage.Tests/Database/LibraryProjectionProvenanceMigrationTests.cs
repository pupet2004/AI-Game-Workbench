using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;
using Workbench.Storage.Memory;

namespace Workbench.Storage.Tests.Database;

public sealed class LibraryProjectionProvenanceMigrationTests
{
    [Fact]
    public async Task Latest_schema_allows_manual_proposals_without_session_and_preserves_old_session_rows()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        Assert.Equal(22L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));
        Assert.Equal(0L, await ScalarAsync<long>(connection,
            "SELECT \"notnull\" FROM pragma_table_info('project_library_proposals') WHERE name='source_session_id';"));

        var projectId = Guid.NewGuid();
        await ExecuteAsync(connection, "INSERT INTO projects(id,name,root_path,project_type,created_at,last_opened_at) VALUES($id,'P','C:/P',0,$at,$at);",
            ("$id", projectId.ToString()), ("$at", "2026-08-25T00:00:00+00:00"));
        var proposal = new ProjectLibraryProposalDraft(
            Guid.NewGuid(), projectId, null, LibraryProposalAction.CreateNode, null, null, null, 0,
            "Design", "Manual", new DateOnly(2026, 8, 25), "Manual", null, [],
            DateTimeOffset.Parse("2026-08-25T00:00:00+00:00"));
        var service = new ProjectLibraryProposalService(database);

        var stored = await service.CreateProposalAsync(proposal);

        Assert.Null(stored.SourceSessionId);
        Assert.Null((await service.GetAsync(projectId, proposal.ProposalId))!.SourceSessionId);
    }

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("No scalar."));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object Value)[] values)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var value in values) command.Parameters.AddWithValue(value.Name, value.Value);
        await command.ExecuteNonQueryAsync();
    }
}
