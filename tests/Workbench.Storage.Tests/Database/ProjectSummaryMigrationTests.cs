using System.Globalization;
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
        Assert.Equal(20L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));
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
        Assert.Equal(20L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));

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
        Assert.Equal(20L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));

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
        Assert.Equal(20L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));
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
        Assert.Equal(20L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));

        Assert.Equal(
            ["result_id", "summary_delta_payload_json", "summary_persisted_at"],
            await StringsAsync(connection, "SELECT name FROM pragma_table_info('leader_messages') WHERE name IN ('result_id','summary_delta_payload_json','summary_persisted_at') ORDER BY cid;"));

        Assert.Equal(
            [
                NormalizeSql("""result_id TEXT NULL"""),
                NormalizeSql("""
                    summary_delta_payload_json TEXT NULL
                        CHECK(
                            (summary_delta_payload_json IS NULL AND result_id IS NULL) OR
                            (summary_delta_payload_json IS NOT NULL AND result_id IS NOT NULL AND role = 'assistant'))
                    """),
                NormalizeSql("""
                    summary_persisted_at TEXT NULL
                        CHECK(
                            summary_persisted_at IS NULL OR
                            (result_id IS NOT NULL AND summary_delta_payload_json IS NOT NULL AND role = 'assistant'))
                    """)
            ],
            TopLevelTableClauses(await SchemaSqlAsync(connection, "table", "leader_messages"))
                .Where(IsAuthorizedLeaderSummaryColumn)
                .ToArray());

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "INSERT INTO leader_messages(epoch_id,sequence,role,text,created_at,result_id,summary_delta_payload_json) VALUES($epoch,2,'user','user',$at,$result,'[]');", ("$epoch", fixture.EpochId), ("$at", V18Fixture.At), ("$result", Guid.NewGuid())));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "INSERT INTO leader_messages(epoch_id,sequence,role,text,created_at,result_id) VALUES($epoch,2,'assistant','assistant',$at,$result);", ("$epoch", fixture.EpochId), ("$at", V18Fixture.At), ("$result", Guid.NewGuid())));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "INSERT INTO leader_messages(epoch_id,sequence,role,text,created_at,summary_delta_payload_json) VALUES($epoch,2,'assistant','assistant',$at,'[]');", ("$epoch", fixture.EpochId), ("$at", V18Fixture.At)));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, "INSERT INTO leader_messages(epoch_id,sequence,role,text,created_at,summary_persisted_at) VALUES($epoch,2,'assistant','assistant',$at,$persisted);", ("$epoch", fixture.EpochId), ("$at", V18Fixture.At), ("$persisted", "2026-08-18T12:01:00.0000000+00:00")));
        await ExecuteAsync(connection, "INSERT INTO leader_messages(epoch_id,sequence,role,text,created_at,result_id,summary_delta_payload_json) VALUES($epoch,2,'assistant','assistant',$at,$result,'[]');", ("$epoch", fixture.EpochId), ("$at", V18Fixture.At), ("$result", Guid.NewGuid()));
        await ExecuteAsync(connection, "INSERT INTO leader_messages(epoch_id,sequence,role,text,created_at,result_id,summary_delta_payload_json,summary_persisted_at) VALUES($epoch,3,'assistant','persisted assistant',$at,$result,'[]',$persisted);", ("$epoch", fixture.EpochId), ("$at", V18Fixture.At), ("$result", Guid.NewGuid()), ("$persisted", "2026-08-18T12:01:00.0000000+00:00"));

        Assert.Equal(
            "assistant|[]|<NULL>|persisted assistant|[]|2026-08-18T12:01:00.0000000+00:00",
            string.Join('|', await RowsAsync(connection, "SELECT text,summary_delta_payload_json,summary_persisted_at FROM leader_messages WHERE sequence IN (2,3) ORDER BY sequence;")));
    }

    [Fact]
    public async Task Migration019_preserves_existing_leader_messages()
    {
        await using var fixture = await V18Fixture.CreateAsync();

        await fixture.Database.InitializeAsync();

        await using var connection = fixture.Database.CreateConnection();
        await connection.OpenAsync();
        Assert.Equal(20L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));
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
    public async Task Migration_preserves_legacy_rows_and_job_states_while_adding_only_v19_summary_objects()
    {
        await using var fixture = await V18Fixture.CreateAsync();

        LegacySnapshot before;
        IReadOnlyList<string> tablesBefore;
        IReadOnlyList<string> leaderColumnsBefore;
        IReadOnlyList<SchemaObjectDefinition> schemaBefore;
        await using (var beforeConnection = fixture.Database.CreateConnection())
        {
            await beforeConnection.OpenAsync();
            before = await ReadLegacySnapshotAsync(beforeConnection);
            tablesBefore = await UserTablesAsync(beforeConnection);
            leaderColumnsBefore = await ColumnSignaturesAsync(beforeConnection, "leader_messages");
            schemaBefore = await SchemaObjectsAsync(beforeConnection);
            Assert.Equal(18L, await ScalarAsync<long>(beforeConnection, "PRAGMA user_version;"));
            Assert.Equal("Completed:2,Pending:0,Running:3", before.SynthesisStates);
        }

        await HistoricalMigrationTestDatabase.InitializeThroughAsync(fixture.Database, 19);

        await using var connection = fixture.Database.CreateConnection();
        await connection.OpenAsync();
        Assert.Equal(19L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));
        Assert.Equal(before, await ReadLegacySnapshotAsync(connection));
        var tablesAfter = await UserTablesAsync(connection);
        var schemaAfter = await SchemaObjectsAsync(connection);
        var schemaAfterByKey = schemaAfter.ToDictionary(item => item.Key, StringComparer.Ordinal);
        foreach (var legacyObject in schemaBefore)
        {
            Assert.True(
                schemaAfterByKey.TryGetValue(legacyObject.Key, out var afterObject),
                $"Legacy schema object {legacyObject.Key} was removed.");
            if (legacyObject.Key == "table|leader_messages")
            {
                Assert.Equal(legacyObject.Type, afterObject.Type);
                Assert.Equal(legacyObject.Name, afterObject.Name);
                Assert.Equal(legacyObject.TableName, afterObject.TableName);
                Assert.Equal(
                    WithoutAuthorizedLeaderSummaryColumns(legacyObject.Sql),
                    WithoutAuthorizedLeaderSummaryColumns(afterObject.Sql));
            }
            else
            {
                Assert.Equal(legacyObject, afterObject);
            }
        }
        var schemaBeforeKeys = schemaBefore.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(
            [
                "index|ix_leader_messages_pending_summary",
                "index|ix_project_summary_entries_project_order",
                "index|ix_project_summary_source_refs_filter",
                "index|ux_project_summary_entries_result_ordinal",
                "table|project_summary_entries",
                "table|project_summary_source_refs"
            ],
            schemaAfter
                .Where(item => !schemaBeforeKeys.Contains(item.Key))
                .Select(item => item.Key)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            ["project_summary_entries", "project_summary_source_refs"],
            tablesAfter.Except(tablesBefore, StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Equal(
            tablesBefore,
            tablesAfter.Where(table =>
                table is not "project_summary_entries" and not "project_summary_source_refs"));
        Assert.Equal(
            [
                "entry_id|TEXT|0|1|NULL",
                "project_id|TEXT|1|0|NULL",
                "occurred_at|TEXT|1|0|NULL",
                "created_at|TEXT|1|0|NULL",
                "kind|TEXT|1|0|NULL",
                "text|TEXT|1|0|NULL",
                "result_id|TEXT|1|0|NULL",
                "delta_ordinal|INTEGER|1|0|NULL"
            ],
            await ColumnSignaturesAsync(connection, "project_summary_entries"));
        Assert.Equal(
            [
                "entry_id|TEXT|1|1|NULL",
                "ordinal|INTEGER|1|2|NULL",
                "source_kind|TEXT|1|0|NULL",
                "source_locator|TEXT|1|0|NULL"
            ],
            await ColumnSignaturesAsync(connection, "project_summary_source_refs"));
        var leaderColumnsAfter = await ColumnSignaturesAsync(connection, "leader_messages");
        Assert.Equal(
            [
                "result_id|TEXT|0|0|NULL",
                "summary_delta_payload_json|TEXT|0|0|NULL",
                "summary_persisted_at|TEXT|0|0|NULL"
            ],
            leaderColumnsAfter.Except(leaderColumnsBefore, StringComparer.Ordinal));
        Assert.Equal(
            leaderColumnsBefore,
            leaderColumnsAfter.Where(column =>
                !column.StartsWith("result_id|", StringComparison.Ordinal) &&
                !column.StartsWith("summary_delta_payload_json|", StringComparison.Ordinal) &&
                !column.StartsWith("summary_persisted_at|", StringComparison.Ordinal)));
        Assert.Equal(
            [
                "ix_project_summary_entries_project_order|0|0|project_id:ASC,occurred_at:DESC,created_at:DESC,entry_id:ASC",
                "ux_project_summary_entries_result_ordinal|1|0|result_id:ASC,delta_ordinal:ASC"
            ],
            await IndexSignaturesAsync(connection, "project_summary_entries"));
        Assert.Equal(
            ["ix_project_summary_source_refs_filter|0|0|source_kind:ASC,source_locator:ASC,entry_id:ASC"],
            await IndexSignaturesAsync(connection, "project_summary_source_refs"));
        Assert.Equal(
            "ix_leader_messages_pending_summary|0|1|summary_persisted_at:ASC,result_id:ASC",
            await IndexSignatureAsync(connection, "ix_leader_messages_pending_summary"));
        Assert.Equal(
            NormalizeSql("""
                CREATE INDEX ix_leader_messages_pending_summary
                ON leader_messages(summary_persisted_at, result_id)
                WHERE result_id IS NOT NULL AND summary_delta_payload_json IS NOT NULL
                """),
            NormalizeSql(await SchemaSqlAsync(connection, "index", "ix_leader_messages_pending_summary")));
        Assert.Equal(
            NormalizeSql("""
                CREATE UNIQUE INDEX ux_project_summary_entries_result_ordinal
                ON project_summary_entries(result_id, delta_ordinal)
                """),
            NormalizeSql(await SchemaSqlAsync(connection, "index", "ux_project_summary_entries_result_ordinal")));
        Assert.Equal(
            NormalizeSql("""
                CREATE INDEX ix_project_summary_entries_project_order
                ON project_summary_entries(project_id, occurred_at DESC, created_at DESC, entry_id)
                """),
            NormalizeSql(await SchemaSqlAsync(connection, "index", "ix_project_summary_entries_project_order")));
        Assert.Equal(
            NormalizeSql("""
                CREATE INDEX ix_project_summary_source_refs_filter
                ON project_summary_source_refs(source_kind, source_locator, entry_id)
                """),
            NormalizeSql(await SchemaSqlAsync(connection, "index", "ix_project_summary_source_refs_filter")));
        var entryTableSql = await ScalarAsync<string>(
            connection,
            "SELECT sql FROM sqlite_master WHERE type='table' AND name='project_summary_entries';");
        Assert.Equal(
            NormalizeSql("""
                CREATE TABLE project_summary_entries (
                    entry_id TEXT PRIMARY KEY,
                    project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
                    occurred_at TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    kind TEXT NOT NULL CHECK(kind IN ('Decision', 'Change', 'Constraint', 'RejectedPath', 'Unresolved')),
                    text TEXT NOT NULL CHECK(length(trim(text)) > 0),
                    result_id TEXT NOT NULL,
                    delta_ordinal INTEGER NOT NULL CHECK(delta_ordinal >= 0)
                )
                """),
            NormalizeSql(entryTableSql));
        var sourceTableSql = await ScalarAsync<string>(
            connection,
            "SELECT sql FROM sqlite_master WHERE type='table' AND name='project_summary_source_refs';");
        Assert.Equal(
            NormalizeSql("""
                CREATE TABLE project_summary_source_refs (
                    entry_id TEXT NOT NULL REFERENCES project_summary_entries(entry_id) ON DELETE CASCADE,
                    ordinal INTEGER NOT NULL CHECK(ordinal >= 0),
                    source_kind TEXT NOT NULL CHECK(length(trim(source_kind)) > 0),
                    source_locator TEXT NOT NULL CHECK(length(trim(source_locator)) > 0),
                    PRIMARY KEY(entry_id, ordinal)
                )
                """),
            NormalizeSql(sourceTableSql));
        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM project_summary_entries;"));
        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM project_summary_source_refs;"));

        string[] allowedKinds = ["Decision", "Change", "Constraint", "RejectedPath", "Unresolved"];
        var entryIds = new List<Guid>();
        var resultIds = new List<Guid>();
        foreach (var kind in allowedKinds)
        {
            var entryId = Guid.NewGuid();
            var resultId = Guid.NewGuid();
            entryIds.Add(entryId);
            resultIds.Add(resultId);
            await ExecuteAsync(
                connection,
                "INSERT INTO project_summary_entries(entry_id,project_id,occurred_at,created_at,kind,text,result_id,delta_ordinal) VALUES($entry,$project,$at,$at,$kind,$kind,$result,0);",
                ("$entry", entryId), ("$project", fixture.ProjectId), ("$at", V18Fixture.At),
                ("$kind", kind), ("$result", resultId));
        }
        Assert.Equal(
            allowedKinds,
            await StringsAsync(connection, "SELECT kind FROM project_summary_entries ORDER BY rowid;"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            connection,
            "INSERT INTO project_summary_entries(entry_id,project_id,occurred_at,created_at,kind,text,result_id,delta_ordinal) VALUES($entry,$project,$at,$at,'ForbiddenKind','text',$result,0);",
            ("$entry", Guid.NewGuid()), ("$project", fixture.ProjectId),
            ("$at", V18Fixture.At), ("$result", Guid.NewGuid())));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            connection,
            "INSERT INTO project_summary_entries(entry_id,project_id,occurred_at,created_at,kind,text,result_id,delta_ordinal) VALUES($entry,$project,$at,$at,'Decision','duplicate replay identity',$result,0);",
            ("$entry", Guid.NewGuid()), ("$project", fixture.ProjectId),
            ("$at", V18Fixture.At), ("$result", resultIds[0])));
        await ExecuteAsync(
            connection,
            "INSERT INTO project_summary_source_refs(entry_id,ordinal,source_kind,source_locator) VALUES($entry,0,'LeaderMessage','1');",
            ("$entry", entryIds[0]));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            connection,
            "INSERT INTO project_summary_source_refs(entry_id,ordinal,source_kind,source_locator) VALUES($entry,0,'Task','2');",
            ("$entry", entryIds[0])));
    }

    private static async Task<LegacySnapshot> ReadLegacySnapshotAsync(SqliteConnection connection) =>
        new(
            string.Join('\n', await RowsAsync(connection, "SELECT * FROM projects ORDER BY id;")),
            string.Join('\n', await RowsAsync(connection, "SELECT * FROM project_leaders ORDER BY project_id;")),
            string.Join('\n', await RowsAsync(connection, "SELECT * FROM leader_session_epochs ORDER BY id;")),
            string.Join('\n', await RowsAsync(connection, "SELECT id,epoch_id,sequence,role,text,created_at FROM leader_messages ORDER BY id;")),
            string.Join('\n', await RowsAsync(connection, "SELECT * FROM project_memory_items ORDER BY id;")),
            string.Join('\n', await RowsAsync(connection, "SELECT * FROM project_memory_sources ORDER BY memory_id,source_type,source_ref;")),
            string.Join('\n', await RowsAsync(connection, "SELECT * FROM project_memory_synthesis_jobs ORDER BY epoch_id;")),
            string.Join(',', await StringsAsync(connection, "SELECT status || ':' || attempt_count FROM project_memory_synthesis_jobs ORDER BY status;")));

    private static Task<IReadOnlyList<string>> UserTablesAsync(SqliteConnection connection) =>
        StringsAsync(connection, "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;");

    private static async Task<IReadOnlyList<SchemaObjectDefinition>> SchemaObjectsAsync(
        SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT type, name, tbl_name, sql
            FROM sqlite_master
            WHERE type IN ('table','index','trigger','view')
              AND name NOT LIKE 'sqlite_%'
              AND sql IS NOT NULL
            ORDER BY type, name;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var objects = new List<SchemaObjectDefinition>();
        while (await reader.ReadAsync())
        {
            objects.Add(new SchemaObjectDefinition(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                NormalizeSql(reader.GetString(3))));
        }
        return objects;
    }

    private static Task<IReadOnlyList<string>> ColumnSignaturesAsync(
        SqliteConnection connection,
        string table) =>
        StringsAsync(
            connection,
            $"SELECT name || '|' || upper(type) || '|' || \"notnull\" || '|' || pk || '|' || coalesce(dflt_value,'NULL') FROM pragma_table_info('{table}') ORDER BY cid;");

    private static Task<string> SchemaSqlAsync(
        SqliteConnection connection,
        string type,
        string name) =>
        ScalarAsync<string>(
            connection,
            "SELECT sql FROM sqlite_master WHERE type=$type AND name=$name;",
            ("$type", type), ("$name", name));

    private static string NormalizeSql(string sql) =>
        string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static IReadOnlyList<string> TopLevelTableClauses(string sql)
    {
        var normalized = NormalizeSql(sql);
        var open = normalized.IndexOf('(');
        var close = normalized.LastIndexOf(')');
        Assert.True(open >= 0 && close > open);
        var body = normalized[(open + 1)..close];
        var clauses = new List<string>();
        var depth = 0;
        var start = 0;
        for (var index = 0; index < body.Length; index++)
        {
            switch (body[index])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    clauses.Add(NormalizeSql(body[start..index]));
                    start = index + 1;
                    break;
            }
        }
        clauses.Add(NormalizeSql(body[start..]));
        return clauses;
    }

    private static bool IsAuthorizedLeaderSummaryColumn(string clause) =>
        clause.StartsWith("result_id ", StringComparison.OrdinalIgnoreCase) ||
        clause.StartsWith("summary_delta_payload_json ", StringComparison.OrdinalIgnoreCase) ||
        clause.StartsWith("summary_persisted_at ", StringComparison.OrdinalIgnoreCase);

    private static string WithoutAuthorizedLeaderSummaryColumns(string sql)
    {
        var normalized = NormalizeSql(sql);
        var open = normalized.IndexOf('(');
        var close = normalized.LastIndexOf(')');
        Assert.True(open >= 0 && close > open);
        var legacyColumns = TopLevelTableClauses(normalized).Where(column =>
            !IsAuthorizedLeaderSummaryColumn(column));
        return NormalizeSql(
            $"{normalized[..open].Trim()} ({string.Join(", ", legacyColumns)}) {normalized[(close + 1)..].Trim()}");
    }

    private static async Task<IReadOnlyList<string>> IndexSignaturesAsync(
        SqliteConnection connection,
        string table)
    {
        var names = await StringsAsync(
            connection,
            $"SELECT name FROM pragma_index_list('{table}') WHERE origin='c' ORDER BY name;");
        var signatures = new List<string>();
        foreach (var name in names)
        {
            signatures.Add(await IndexSignatureAsync(connection, name));
        }
        return signatures;
    }

    private static async Task<string> IndexSignatureAsync(
        SqliteConnection connection,
        string indexName)
    {
        var header = connection.CreateCommand();
        header.CommandText = "SELECT \"unique\", partial FROM pragma_index_list((SELECT tbl_name FROM sqlite_master WHERE type='index' AND name=$name)) WHERE name=$name;";
        header.Parameters.AddWithValue("$name", indexName);
        await using var headerReader = await header.ExecuteReaderAsync();
        Assert.True(await headerReader.ReadAsync());
        var unique = headerReader.GetInt64(0);
        var partial = headerReader.GetInt64(1);
        await headerReader.DisposeAsync();

        var columns = connection.CreateCommand();
        columns.CommandText = $"SELECT name || CASE \"desc\" WHEN 1 THEN ':DESC' ELSE ':ASC' END FROM pragma_index_xinfo('{indexName}') WHERE key=1 ORDER BY seqno;";
        var columnSignatures = new List<string>();
        await using var reader = await columns.ExecuteReaderAsync();
        while (await reader.ReadAsync()) columnSignatures.Add(reader.GetString(0));
        return $"{indexName}|{unique}|{partial}|{string.Join(',', columnSignatures)}";
    }

    private static async Task<IReadOnlyList<string>> RowsAsync(
        SqliteConnection connection,
        string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            var values = new string[reader.FieldCount];
            for (var index = 0; index < reader.FieldCount; index++)
            {
                values[index] = reader.IsDBNull(index)
                    ? "<NULL>"
                    : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture)!;
            }
            rows.Add(string.Join('|', values));
        }
        return rows;
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

    private sealed record LegacySnapshot(
        string Projects,
        string ProjectLeaders,
        string Epochs,
        string LeaderMessages,
        string MemoryItems,
        string MemorySources,
        string SynthesisJobs,
        string SynthesisStates);

    private sealed record SchemaObjectDefinition(
        string Type,
        string Name,
        string TableName,
        string Sql)
    {
        public string Key => $"{Type}|{Name}";
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
            var archivedEpochs = new[]
            {
                new StoredLeaderSessionEpoch(
                    Guid.NewGuid(), projectId, "codex", Guid.NewGuid(), "gpt-test",
                    Guid.NewGuid(), "pending-thread", "C:/Migration", at.AddDays(-2),
                    at.AddDays(-2).AddHours(1), at.AddDays(-2).AddHours(1), "seed", null),
                new StoredLeaderSessionEpoch(
                    Guid.NewGuid(), projectId, "codex", Guid.NewGuid(), "gpt-test",
                    Guid.NewGuid(), "completed-thread", "C:/Migration", at.AddDays(-1),
                    at.AddDays(-1).AddHours(1), at.AddDays(-1).AddHours(1), "seed", null)
            };
            var epochs = new LeaderSessionEpochRepository(database);
            foreach (var archivedEpoch in archivedEpochs)
            {
                await epochs.SaveAsync(archivedEpoch);
            }
            var message = await new LeaderMessageRepository(database).AppendAsync(epochId, "assistant", "existing visible reply", at);

            await using var connection = database.CreateConnection();
            await connection.OpenAsync();
            var memoryId = Guid.NewGuid();
            await ExecuteAsync(connection, "INSERT INTO project_memory_items(id,project_id,layer,topic,content,status,created_at,updated_at,certified_at) VALUES($id,$project,'Daily','topic','legacy content','Active',$at,$at,NULL);", ("$id", memoryId), ("$project", projectId), ("$at", At));
            await ExecuteAsync(connection, "INSERT INTO project_memory_sources(memory_id,source_type,source_ref) VALUES($id,'LeaderMessage','1');", ("$id", memoryId));
            await ExecuteAsync(connection, "INSERT INTO project_memory_synthesis_jobs(epoch_id,project_id,status,attempt_count,last_attempted_at,completed_at,last_error,created_at,updated_at) VALUES($epoch,$project,'Running',3,$at,NULL,NULL,$at,$at);", ("$epoch", epochId), ("$project", projectId), ("$at", At));
            await ExecuteAsync(connection, "INSERT INTO project_memory_synthesis_jobs(epoch_id,project_id,status,attempt_count,last_attempted_at,completed_at,last_error,created_at,updated_at) VALUES($epoch,$project,'Pending',0,NULL,NULL,NULL,$at,$at);", ("$epoch", archivedEpochs[0].Id), ("$project", projectId), ("$at", At));
            await ExecuteAsync(connection, "INSERT INTO project_memory_synthesis_jobs(epoch_id,project_id,status,attempt_count,last_attempted_at,completed_at,last_error,created_at,updated_at) VALUES($epoch,$project,'Completed',2,$at,$at,NULL,$at,$at);", ("$epoch", archivedEpochs[1].Id), ("$project", projectId), ("$at", At));
            return new V18Fixture(temporary, database, projectId, epochId, message.Id);
        }

        public ValueTask DisposeAsync() => _temporary.DisposeAsync();
    }
}
