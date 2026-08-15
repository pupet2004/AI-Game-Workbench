using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Tests.Database;

public sealed class TypedLeaderReviewBackfillMigrationTests
{
    [Fact]
    public async Task Fresh_database_reaches_v18_and_is_idempotent()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        Assert.Equal(18L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));
        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }

    [Fact]
    public async Task Legacy_e1_e2a_e2b_events_backfill_typed_projection_and_preserve_source_loss()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var fixture = await PrepareV16FixtureAsync(database);
        await AppendAsync(database, fixture, fixture.DecisionPass, "LeaderReviewDecisionRecorded", $"{{\"TaskRevisionId\":\"{fixture.Revision}\",\"FinalReportEventId\":\"{Guid.NewGuid()}\",\"Outcome\":\"Pass\",\"ActionLevel\":\"L1LocalFix\",\"ReviewDepth\":\"ReportOnly\",\"Summary\":\"pass\",\"NextAction\":\"none\"}}", fixture.At);
        await AppendAsync(database, fixture, fixture.DecisionAsk, "LeaderReviewDecisionRecorded", $"{{\"TaskRevisionId\":\"{fixture.Revision}\",\"FinalReportEventId\":\"{Guid.NewGuid()}\",\"Outcome\":\"AskUser\",\"ActionLevel\":\"L3DecisionRequired\",\"ReviewDepth\":\"ReportOnly\",\"Summary\":\"ask\",\"NextAction\":\"choose\"}}", fixture.At);
        var pass = await ReadEventPayloadAsync(database, fixture.DecisionPass);
        var ask = await ReadEventPayloadAsync(database, fixture.DecisionAsk);
        await AppendAsync(database, fixture, Guid.NewGuid(), "AssignmentAutoCompleted", $"{{\"ReviewDecisionEventId\":\"{fixture.DecisionPass}\",\"TaskRevisionId\":\"{fixture.Revision}\",\"FinalReportEventId\":\"{pass}\",\"Outcome\":\"Pass\",\"Authority\":\"Autonomous\",\"Resolution\":\"AutoProceed\"}}", fixture.At);
        await AppendAsync(database, fixture, fixture.GateEvent, "AssignmentNeedsUserDecision", $"{{\"ReviewDecisionEventId\":\"{fixture.DecisionAsk}\",\"TaskRevisionId\":\"{fixture.Revision}\",\"FinalReportEventId\":\"{ask}\",\"Resolution\":\"AskUser\"}}", fixture.At);
        await AppendAsync(database, fixture, fixture.ResponseEvent, "LeaderReviewUserResponseReceived", $"{{\"TaskRevisionId\":\"{fixture.Revision}\",\"ReviewDecisionEventId\":\"{fixture.DecisionAsk}\",\"QuestionMessageId\":998,\"UserMessageId\":1000}}", fixture.At.AddMinutes(1));

        await database.InitializeAsync();
        await using var connection = database.CreateConnection(); await connection.OpenAsync();
        Assert.Equal(18L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));
        Assert.Equal(("Autonomous", "Recorded"), await ReadAuthorityAsync(connection, fixture.DecisionPass));
        Assert.Equal(("", "LegacyNotRecorded"), await ReadAuthorityAsync(connection, fixture.DecisionAsk));
        Assert.Equal(("Responded", (long?)null, (long?)null), await ReadGateAsync(connection, fixture.DecisionAsk));
    }

    [Fact]
    public async Task Malformed_known_decision_rolls_back_all_backfill_rows_and_keeps_v16()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var fixture = await PrepareV16FixtureAsync(database);
        var validDecision = Guid.NewGuid();
        var validReport = Guid.NewGuid();
        await AppendAsync(database, fixture, validDecision, "LeaderReviewDecisionRecorded", $"{{\"TaskRevisionId\":\"{fixture.Revision}\",\"FinalReportEventId\":\"{validReport}\",\"Outcome\":\"Pass\",\"ActionLevel\":\"L1LocalFix\",\"ReviewDepth\":\"ReportOnly\",\"Summary\":\"pass\",\"NextAction\":\"none\"}}", fixture.At);
        await AppendAsync(database, fixture, Guid.NewGuid(), "AssignmentAutoCompleted", $"{{\"ReviewDecisionEventId\":\"{validDecision}\",\"TaskRevisionId\":\"{fixture.Revision}\",\"FinalReportEventId\":\"{validReport}\",\"Outcome\":\"Pass\",\"Authority\":\"Autonomous\",\"Resolution\":\"AutoProceed\"}}", fixture.At);
        await AppendAsync(database, fixture, fixture.DecisionPass, "LeaderReviewDecisionRecorded", "{\"TaskRevisionId\":\"bad\"}", fixture.At);
        await SetVersionAsync(database, 16);

        await Assert.ThrowsAsync<DatabaseInitializationException>(() => database.InitializeAsync());
        await using var connection = database.CreateConnection(); await connection.OpenAsync();
        Assert.Equal(16L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));
        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM task_review_decisions;"));
    }

    [Fact]
    public async Task Unrelated_events_do_not_create_review_projection()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var fixture = await PrepareV16FixtureAsync(database);
        await AppendAsync(database, fixture, Guid.NewGuid(), "WorkerCompleted", "{\"Message\":\"done\"}", fixture.At);
        await SetVersionAsync(database, 16);
        await database.InitializeAsync();
        await using var connection = database.CreateConnection(); await connection.OpenAsync();
        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM task_review_decisions;"));
    }

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<Fixture> PrepareV16FixtureAsync(WorkbenchDatabase database)
    {
        var fixture = new Fixture(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        await using var connection = database.CreateConnection(); await connection.OpenAsync();
        await ExecuteAsync(connection, "INSERT INTO projects(id,name,root_path,project_type,created_at,last_opened_at) VALUES($p,'P','C:/P',0,$a,$a);", ("$p", fixture.Project.ToString()), ("$a", fixture.At.ToString("O")));
        await ExecuteAsync(connection, "INSERT INTO tasks(id,project_id,title,status,current_revision_id,created_at,updated_at) VALUES($t,$p,'T','Reviewing',$r,$a,$a);", ("$t", fixture.Task.ToString()), ("$p", fixture.Project.ToString()), ("$r", fixture.Revision.ToString()), ("$a", fixture.At.ToString("O")));
        await ExecuteAsync(connection, "INSERT INTO task_revisions(id,task_id,revision_number,goal,scope,out_of_scope,acceptance_json,risk_level,recommended_provider_id,recommended_provider_account_id,recommended_model_profile_id,recommended_agent_runtime_id,change_reason,approved_by,created_at) VALUES($r,$t,1,'G','S','O','[]','Low','p','a','m','rt','initial','leader',$a);", ("$r", fixture.Revision.ToString()), ("$t", fixture.Task.ToString()), ("$a", fixture.At.ToString("O")));
        await ExecuteAsync(connection, "INSERT INTO project_leaders(project_id,current_epoch_id,created_at,updated_at) VALUES($p,NULL,$a,$a);", ("$p", fixture.Project.ToString()), ("$a", fixture.At.ToString("O")));
        await ExecuteAsync(connection, "INSERT INTO leader_session_epochs(id,project_id,provider_id,provider_account_id,model_id,agent_session_id,started_at,last_active_at) VALUES($e,$p,'provider','account','model','session',$a,$a);", ("$e", fixture.Epoch.ToString()), ("$p", fixture.Project.ToString()), ("$a", fixture.At.ToString("O")));
        await ExecuteAsync(connection, "UPDATE project_leaders SET current_epoch_id=$e WHERE project_id=$p;", ("$e", fixture.Epoch.ToString()), ("$p", fixture.Project.ToString()));
        await ExecuteAsync(connection, "INSERT INTO leader_messages(id,epoch_id,sequence,role,text,created_at) VALUES(999,$e,1,'user',$text,$a);", ("$e", fixture.Epoch.ToString()), ("$text", $"Question review-user-decision:{fixture.Project:D}:{fixture.Task:D}:{fixture.DecisionAsk:D}"), ("$a", fixture.At.ToString("O")));
        await ExecuteAsync(connection, "PRAGMA foreign_keys = OFF;");
        await ExecuteAsync(connection, "DROP TABLE task_review_decisions;");
        await ExecuteAsync(connection, """
            CREATE TABLE task_review_decisions (
                review_decision_id TEXT PRIMARY KEY, project_id TEXT NOT NULL, task_id TEXT NOT NULL, revision_id TEXT NOT NULL,
                source_event_id TEXT NULL UNIQUE, outcome TEXT NOT NULL, action_level TEXT NOT NULL, authority_mode TEXT NULL,
                authority_resolution TEXT NOT NULL, authority_mode_recording TEXT NOT NULL, created_at TEXT NOT NULL
            );
            CREATE UNIQUE INDEX ux_task_review_decisions_identity
            ON task_review_decisions(review_decision_id, project_id, task_id, revision_id);
            """);
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;");
        await SetVersionAsync(database, 16);
        return fixture;
    }

    private static async Task AppendAsync(WorkbenchDatabase database, Fixture fixture, Guid id, string type, string payload, DateTimeOffset at)
    {
        await using var connection = database.CreateConnection(); await connection.OpenAsync();
        await ExecuteAsync(connection, "INSERT INTO task_events(id,project_id,task_id,execution_id,event_type,payload_json,created_at) VALUES($i,$p,$t,NULL,$type,$payload,$at);", ("$i", id.ToString()), ("$p", fixture.Project.ToString()), ("$t", fixture.Task.ToString()), ("$type", type), ("$payload", payload), ("$at", at.ToString("O")));
    }

    private static async Task SetVersionAsync(WorkbenchDatabase database, long version)
    {
        await using var connection = database.CreateConnection(); await connection.OpenAsync();
        await ExecuteAsync(connection, $"PRAGMA user_version = {version};");
    }

    private static async Task<Guid> ReadEventPayloadAsync(WorkbenchDatabase database, Guid eventId)
    {
        await using var connection = database.CreateConnection(); await connection.OpenAsync();
        var command = connection.CreateCommand(); command.CommandText = "SELECT payload_json FROM task_events WHERE id=$id;"; command.Parameters.AddWithValue("$id", eventId.ToString());
        using var json = System.Text.Json.JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
        return Guid.Parse(json.RootElement.GetProperty("FinalReportEventId").GetString()!);
    }

    private static async Task<(string Authority, string Recording)> ReadAuthorityAsync(SqliteConnection connection, Guid decisionId)
    {
        var command = connection.CreateCommand(); command.CommandText = "SELECT COALESCE(authority_mode,''),authority_mode_recording FROM task_review_decisions WHERE review_decision_id=$id;"; command.Parameters.AddWithValue("$id", decisionId.ToString());
        await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync()); return (reader.GetString(0), reader.GetString(1));
    }

    private static async Task<(string State, long? Question, long? User)> ReadGateAsync(SqliteConnection connection, Guid decisionId)
    {
        var command = connection.CreateCommand(); command.CommandText = "SELECT state,question_message_id,user_message_id FROM task_review_user_gates WHERE review_decision_id=$id;"; command.Parameters.AddWithValue("$id", decisionId.ToString());
        await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync()); return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt64(2));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand(); command.CommandText = sql; foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value); await command.ExecuteNonQueryAsync();
    }

    private sealed record Fixture(Guid Project, Guid Task, Guid Revision, Guid Epoch, Guid DecisionPass, Guid DecisionAsk, DateTimeOffset At)
    {
        public Guid GateEvent { get; } = Guid.NewGuid();
        public Guid ResponseEvent { get; } = Guid.NewGuid();
    }
}
