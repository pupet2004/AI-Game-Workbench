using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Tests.Database;

public sealed class HistoricalAuthorityRecordingMigrationTests
{
    [Fact]
    public async Task Fresh_database_reaches_v17_with_explicit_authority_recording()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        Assert.Equal(17L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));

        var columns = await ColumnsAsync(connection, "task_review_decisions");
        Assert.Contains(columns, column => column.Name == "authority_mode" && !column.NotNull);
        Assert.Contains(columns, column => column.Name == "authority_mode_recording" && column.NotNull);
    }

    [Fact]
    public async Task V15_to_v17_preserves_recorded_decisions_and_leaves_gate_schema_and_rows_unchanged()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        await PrepareV15SchemaAsync(database);

        await using (var connection = database.CreateConnection())
        {
            await connection.OpenAsync();
            await CreateFixtureAsync(connection);
            await InsertV15DecisionAsync(connection, "recorded");
            await InsertGateAsync(connection, "recorded");
        }

        await using var before = database.CreateConnection();
        await before.OpenAsync();
        var gateSql = await ScalarAsync<string>(before, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'task_review_user_gates';");

        await database.InitializeAsync();
        await database.InitializeAsync();

        await using var verified = database.CreateConnection();
        await verified.OpenAsync();
        Assert.Equal(17L, await ScalarAsync<long>(verified, "PRAGMA user_version;"));
        Assert.Equal(("Balanced", "Recorded"), await ReadAuthorityAsync(verified, "recorded"));
        Assert.Equal(1L, await ScalarAsync<long>(verified, "SELECT COUNT(*) FROM task_review_user_gates WHERE review_decision_id = 'recorded';"));
        Assert.Equal(gateSql, await ScalarAsync<string>(verified, "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'task_review_user_gates';"));
        Assert.Equal(0L, await ScalarAsync<long>(verified, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }

    [Fact]
    public async Task V16_authority_recording_check_accepts_only_honest_combinations()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        await CreateFixtureAsync(connection);

        await InsertV16DecisionAsync(connection, "recorded-valid", "Balanced", "Recorded");
        await InsertV16DecisionAsync(connection, "legacy-valid", null, "LegacyNotRecorded");
        await Assert.ThrowsAsync<SqliteException>(() => InsertV16DecisionAsync(connection, "recorded-null", null, "Recorded"));
        await Assert.ThrowsAsync<SqliteException>(() => InsertV16DecisionAsync(connection, "legacy-mode", "Balanced", "LegacyNotRecorded"));
        await Assert.ThrowsAsync<SqliteException>(() => InsertV16DecisionAsync(connection, "invalid-recording", "Balanced", "Unknown"));
        await Assert.ThrowsAsync<SqliteException>(() => InsertV16DecisionAsync(connection, "invalid-mode", "Unknown", "Recorded"));
    }

    private static async Task CreateFixtureAsync(SqliteConnection connection)
    {
        const string at = "2026-08-15T00:00:00+00:00";
        await ExecuteAsync(connection, "INSERT INTO projects (id,name,root_path,project_type,created_at,last_opened_at) VALUES ('p','P','C:/P',0,$at,$at);", ("$at", at));
        await ExecuteAsync(connection, "INSERT INTO tasks (id,project_id,title,status,current_revision_id,created_at,updated_at) VALUES ('t','p','T','Draft','r',$at,$at);", ("$at", at));
        await ExecuteAsync(connection, "INSERT INTO task_revisions (id,task_id,revision_number,goal,scope,out_of_scope,acceptance_json,risk_level,recommended_provider_id,recommended_provider_account_id,recommended_model_profile_id,recommended_agent_runtime_id,change_reason,approved_by,created_at) VALUES ('r','t',1,'G','S','O','[]','Low','p','a','m','rt','initial','leader',$at);", ("$at", at));
    }

    private static async Task PrepareV15SchemaAsync(WorkbenchDatabase database)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        await ExecuteAsync(connection, "PRAGMA foreign_keys = OFF;");
        await ExecuteAsync(connection, "DROP TABLE task_review_decisions;");
        await ExecuteAsync(connection, """
            CREATE TABLE task_review_decisions (
                review_decision_id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL,
                task_id TEXT NOT NULL,
                revision_id TEXT NOT NULL,
                source_event_id TEXT NULL UNIQUE,
                outcome TEXT NOT NULL CHECK(outcome IN ('Pass', 'Fix', 'Continue', 'AskUser')),
                action_level TEXT NOT NULL CHECK(action_level IN ('L1LocalFix', 'L2TaskRework', 'L3DecisionRequired')),
                authority_mode TEXT NOT NULL CHECK(authority_mode IN ('Cautious', 'Balanced', 'Autonomous')),
                authority_resolution TEXT NOT NULL CHECK(authority_resolution IN ('AutoProceed', 'NotifyAndProceed', 'AskUser')),
                created_at TEXT NOT NULL,
                FOREIGN KEY(project_id) REFERENCES projects(id) ON DELETE CASCADE,
                FOREIGN KEY(task_id, project_id) REFERENCES tasks(id, project_id) ON DELETE CASCADE,
                FOREIGN KEY(revision_id, task_id) REFERENCES task_revisions(id, task_id) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX ux_task_review_decisions_identity
            ON task_review_decisions(review_decision_id, project_id, task_id, revision_id);
            CREATE INDEX ix_task_review_decisions_project_task_created
            ON task_review_decisions(project_id, task_id, created_at, review_decision_id);
            PRAGMA user_version = 15;
            """);
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;");
    }

    private static Task InsertV15DecisionAsync(SqliteConnection connection, string id) =>
        ExecuteAsync(connection, "INSERT INTO task_review_decisions (review_decision_id,project_id,task_id,revision_id,outcome,action_level,authority_mode,authority_resolution,created_at) VALUES ($id,'p','t','r','Pass','L1LocalFix','Balanced','AutoProceed','2026-08-15T00:01:00+00:00');", ("$id", id));

    private static Task InsertV16DecisionAsync(SqliteConnection connection, string id, string? authorityMode, string recording) =>
        ExecuteAsync(connection, "INSERT INTO task_review_decisions (review_decision_id,project_id,task_id,revision_id,outcome,action_level,authority_mode,authority_resolution,authority_mode_recording,created_at) VALUES ($id,'p','t','r','Pass','L1LocalFix',$mode,'AutoProceed',$recording,'2026-08-15T00:01:00+00:00');", ("$id", id), ("$mode", authorityMode), ("$recording", recording));

    private static Task InsertGateAsync(SqliteConnection connection, string decisionId) =>
        ExecuteAsync(connection, "INSERT INTO task_review_user_gates (review_decision_id,project_id,task_id,revision_id,question_message_id,user_message_id,opened_at,responded_at,state) VALUES ($id,'p','t','r',NULL,NULL,'2026-08-15T00:01:00+00:00',NULL,'Open');", ("$id", decisionId));

    private static async Task<(string? AuthorityMode, string Recording)> ReadAuthorityAsync(SqliteConnection connection, string id)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT authority_mode,authority_mode_recording FROM task_review_decisions WHERE review_decision_id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetString(1));
    }

    private static async Task<IReadOnlyList<Column>> ColumnsAsync(SqliteConnection connection, string table)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<Column>();
        while (await reader.ReadAsync()) columns.Add(new(reader.GetString(1), reader.GetInt64(3) != 0));
        return columns;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private sealed record Column(string Name, bool NotNull);
}
