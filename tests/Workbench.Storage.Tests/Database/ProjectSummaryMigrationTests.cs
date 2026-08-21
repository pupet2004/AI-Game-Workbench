using Microsoft.Data.Sqlite;
using Workbench.Core.Projects;
using Workbench.Storage.Database;
using Workbench.Storage.Leaders;
using Workbench.Storage.Projects;

namespace Workbench.Storage.Tests.Database;

public sealed class ProjectSummaryMigrationTests
{
    [Fact]
    public async Task Real_v18_schema_migrates_to_v19_once_without_duplicate_columns()
    {
        await using var fixture = await V18Fixture.CreateAsync();

        await using (var before = fixture.Database.CreateConnection())
        {
            await before.OpenAsync();
            Assert.Equal(18L, await ScalarAsync<long>(before, "PRAGMA user_version;"));
            Assert.Equal(0L, await ScalarAsync<long>(before, "SELECT COUNT(*) FROM pragma_table_info('leader_messages') WHERE name IN ('result_id','summary_delta_payload_json','summary_persisted_at');"));
        }

        await fixture.Database.InitializeAsync();
        await fixture.Database.InitializeAsync();

        await using var connection = fixture.Database.CreateConnection();
        await connection.OpenAsync();
        Assert.Equal(19L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));
        Assert.Equal(
            ["project_summary_entries", "project_summary_source_refs"],
            await StringsAsync(connection, "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'project_summary_%' ORDER BY name;"));
        Assert.Equal(
            ["ix_project_summary_entries_project_order", "ix_project_summary_source_refs_filter", "ux_project_summary_entries_result_ordinal"],
            await StringsAsync(connection, "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name LIKE 'project_summary_%' AND sql IS NOT NULL ORDER BY name;"));
        Assert.Equal(
            ["ix_leader_messages_pending_summary"],
            await StringsAsync(connection, "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='leader_messages' AND name='ix_leader_messages_pending_summary';"));
    }

    [Fact]
    public async Task Summary_tables_enforce_entry_and_source_foreign_keys()
    {
        await using var fixture = await V18Fixture.CreateAsync();
        await fixture.Database.InitializeAsync();
        await using var connection = fixture.Database.CreateConnection();
        await connection.OpenAsync();
        Assert.Equal(19L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "INSERT INTO project_summary_entries(entry_id,project_id,occurred_at,created_at,kind,text,result_id,delta_ordinal) VALUES($entry,$project,$at,$at,'Decision','text',$result,0);", ("$entry", Guid.NewGuid()), ("$project", Guid.NewGuid()), ("$at", V18Fixture.At), ("$result", Guid.NewGuid())));

        var entryId = Guid.NewGuid();
        await ExecuteAsync(connection, "INSERT INTO project_summary_entries(entry_id,project_id,occurred_at,created_at,kind,text,result_id,delta_ordinal) VALUES($entry,$project,$at,$at,'Decision','text',$result,0);", ("$entry", entryId), ("$project", fixture.ProjectId), ("$at", V18Fixture.At), ("$result", Guid.NewGuid()));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "INSERT INTO project_summary_source_refs(entry_id,ordinal,source_kind,source_locator) VALUES($entry,0,'LeaderMessage','42');", ("$entry", Guid.NewGuid())));

        await ExecuteAsync(connection, "DELETE FROM projects WHERE id=$project;", ("$project", fixture.ProjectId));
        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM project_summary_entries;"));
    }

    [Theory]
    [InlineData("Unknown", "text", 0)]
    [InlineData("Decision", " ", 0)]
    [InlineData("Decision", "text", -1)]
    public async Task Summary_entry_constraints_reject_invalid_rows(string kind, string text, int ordinal)
    {
        await using var fixture = await V18Fixture.CreateAsync();
        await fixture.Database.InitializeAsync();
        await using var connection = fixture.Database.CreateConnection();
        await connection.OpenAsync();
        Assert.Equal(19L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "INSERT INTO project_summary_entries(entry_id,project_id,occurred_at,created_at,kind,text,result_id,delta_ordinal) VALUES($entry,$project,$at,$at,$kind,$text,$result,$ordinal);", ("$entry", Guid.NewGuid()), ("$project", fixture.ProjectId), ("$at", V18Fixture.At), ("$kind", kind), ("$text", text), ("$result", Guid.NewGuid()), ("$ordinal", ordinal)));
    }

    [Theory]
    [InlineData(-1, "LeaderMessage", "42")]
    [InlineData(0, " ", "42")]
    [InlineData(0, "LeaderMessage", " ")]
    public async Task Summary_source_constraints_reject_invalid_rows(int ordinal, string sourceKind, string sourceLocator)
    {
        await using var fixture = await V18Fixture.CreateAsync();
        await fixture.Database.InitializeAsync();
        await using var connection = fixture.Database.CreateConnection();
        await connection.OpenAsync();
        Assert.Equal(19L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));
        var entryId = Guid.NewGuid();
        await ExecuteAsync(connection, "INSERT INTO project_summary_entries(entry_id,project_id,occurred_at,created_at,kind,text,result_id,delta_ordinal) VALUES($entry,$project,$at,$at,'Decision','text',$result,0);", ("$entry", entryId), ("$project", fixture.ProjectId), ("$at", V18Fixture.At), ("$result", Guid.NewGuid()));

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "INSERT INTO project_summary_source_refs(entry_id,ordinal,source_kind,source_locator) VALUES($entry,$ordinal,$kind,$locator);", ("$entry", entryId), ("$ordinal", ordinal), ("$kind", sourceKind), ("$locator", sourceLocator)));
    }

    [Fact]
    public async Task Migration019_adds_durable_leader_summary_metadata_columns()
    {
        await using var fixture = await V18Fixture.CreateAsync();
        await fixture.Database.InitializeAsync();
        await using var connection = fixture.Database.CreateConnection();
        await connection.OpenAsync();
        Assert.Equal(19L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));

        Assert.Equal(
            ["result_id", "summary_delta_payload_json", "summary_persisted_at"],
            await StringsAsync(connection, "SELECT name FROM pragma_table_info('leader_messages') WHERE name IN ('result_id','summary_delta_payload_json','summary_persisted_at') ORDER BY cid;"));

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "INSERT INTO leader_messages(epoch_id,sequence,role,text,created_at,result_id,summary_delta_payload_json) VALUES($epoch,2,'user','user',$at,$result,'[]');", ("$epoch", fixture.EpochId), ("$at", V18Fixture.At), ("$result", Guid.NewGuid())));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "INSERT INTO leader_messages(epoch_id,sequence,role,text,created_at,result_id) VALUES($epoch,2,'assistant','assistant',$at,$result);", ("$epoch", fixture.EpochId), ("$at", V18Fixture.At), ("$result", Guid.NewGuid())));
        await ExecuteAsync(connection, "INSERT INTO leader_messages(epoch_id,sequence,role,text,created_at,result_id,summary_delta_payload_json) VALUES($epoch,2,'assistant','assistant',$at,$result,'[]');", ("$epoch", fixture.EpochId), ("$at", V18Fixture.At), ("$result", Guid.NewGuid()));
    }

    [Fact]
    public async Task Migration019_preserves_existing_leader_messages()
    {
        await using var fixture = await V18Fixture.CreateAsync();

        await fixture.Database.InitializeAsync();

        await using var connection = fixture.Database.CreateConnection();
        await connection.OpenAsync();
        Assert.Equal(19L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));
        var command = connection.CreateCommand();
        command.CommandText = "SELECT id,epoch_id,sequence,role,text,created_at,result_id,summary_delta_payload_json,summary_persisted_at FROM leader_messages;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(fixture.MessageId, reader.GetInt64(0));
        Assert.Equal(fixture.EpochId.ToString(), reader.GetString(1));
        Assert.Equal(1L, reader.GetInt64(2));
        Assert.Equal("assistant", reader.GetString(3));
        Assert.Equal("existing visible reply", reader.GetString(4));
        Assert.Equal(V18Fixture.At, reader.GetString(5));
        Assert.True(reader.IsDBNull(6));
        Assert.True(reader.IsDBNull(7));
        Assert.True(reader.IsDBNull(8));
    }

    [Fact]
    public async Task Summary_migration_preserves_legacy_counts_and_synthesis_states()
    {
        await using var fixture = await V18Fixture.CreateAsync();

        await fixture.Database.InitializeAsync();

        await using var connection = fixture.Database.CreateConnection();
        await connection.OpenAsync();
        Assert.Equal(19L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));
        Assert.Equal(1L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM project_memory_items;"));
        Assert.Equal(1L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM project_memory_sources;"));
        Assert.Equal("Running", await ScalarAsync<string>(connection, "SELECT status FROM project_memory_synthesis_jobs WHERE epoch_id=$epoch;", ("$epoch", fixture.EpochId)));
        Assert.Equal(3L, await ScalarAsync<long>(connection, "SELECT attempt_count FROM project_memory_synthesis_jobs WHERE epoch_id=$epoch;", ("$epoch", fixture.EpochId)));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value switch { Guid guid => guid.ToString(), _ => value ?? DBNull.Value });
        }
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value switch { Guid guid => guid.ToString(), _ => value ?? DBNull.Value });
        }
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<IReadOnlyList<string>> StringsAsync(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync()) values.Add(reader.GetString(0));
        return values;
    }

    private sealed class V18Fixture : IAsyncDisposable
    {
        private readonly TemporaryDatabase _temporary;

        private V18Fixture(TemporaryDatabase temporary, WorkbenchDatabase database, Guid projectId, Guid epochId, long messageId)
        {
            _temporary = temporary;
            Database = database;
            ProjectId = projectId;
            EpochId = epochId;
            MessageId = messageId;
        }

        public const string At = "2026-08-18T12:00:00.0000000+00:00";
        public WorkbenchDatabase Database { get; }
        public Guid ProjectId { get; }
        public Guid EpochId { get; }
        public long MessageId { get; }

        public static async Task<V18Fixture> CreateAsync()
        {
            var temporary = new TemporaryDatabase();
            var database = new WorkbenchDatabase(temporary.DatabasePath);
            await HistoricalMigrationTestDatabase.InitializeThroughAsync(database, 18);

            var at = DateTimeOffset.Parse(At);
            var projectId = Guid.NewGuid();
            var epochId = Guid.NewGuid();
            await new ProjectRepository(database).UpsertAsync(new Project(projectId, "Migration project", "C:/Migration", ProjectType.Generic, null, at, at));
            await new ProjectLeaderRepository(database).CreateCurrentEpochAsync(
                new StoredProjectLeader(projectId, null, at, at),
                new StoredLeaderSessionEpoch(epochId, projectId, "codex", Guid.NewGuid(), "gpt-test", Guid.NewGuid(), "thread", "C:/Migration", at, at, null, null, null));
            var message = await new LeaderMessageRepository(database).AppendAsync(epochId, "assistant", "existing visible reply", at);

            await using var connection = database.CreateConnection();
            await connection.OpenAsync();
            var memoryId = Guid.NewGuid();
            await ExecuteAsync(connection, "INSERT INTO project_memory_items(id,project_id,layer,topic,content,status,created_at,updated_at,certified_at) VALUES($id,$project,'Daily','topic','legacy content','Active',$at,$at,NULL);", ("$id", memoryId), ("$project", projectId), ("$at", At));
            await ExecuteAsync(connection, "INSERT INTO project_memory_sources(memory_id,source_type,source_ref) VALUES($id,'LeaderMessage','1');", ("$id", memoryId));
            await ExecuteAsync(connection, "INSERT INTO project_memory_synthesis_jobs(epoch_id,project_id,status,attempt_count,last_attempted_at,completed_at,last_error,created_at,updated_at) VALUES($epoch,$project,'Running',3,$at,NULL,NULL,$at,$at);", ("$epoch", epochId), ("$project", projectId), ("$at", At));
            return new V18Fixture(temporary, database, projectId, epochId, message.Id);
        }

        public ValueTask DisposeAsync() => _temporary.DisposeAsync();
    }
}
