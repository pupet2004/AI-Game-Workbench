using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Tests.Database;

public sealed class TypedLeaderReviewMigrationTests
{
    [Fact]
    public async Task Fresh_database_creates_only_the_two_typed_review_tables_at_v19()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);

        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();

        Assert.Equal(22L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));
        Assert.Equal(2L, await ScalarAsync<long>(connection, """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('task_review_decisions', 'task_review_user_gates');
            """));
        Assert.Equal(2L, await ScalarAsync<long>(connection, """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name LIKE 'task_review_%';
            """));

        var decisionColumns = await TableColumnsAsync(connection, "task_review_decisions");
        Assert.Equal(
            ["review_decision_id", "project_id", "task_id", "revision_id", "source_event_id", "final_report_event_id", "outcome", "action_level", "authority_mode", "authority_resolution", "authority_mode_recording", "created_at"],
            decisionColumns.Select(column => column.Name));
        Assert.Equal("review_decision_id", decisionColumns.Single(column => column.PrimaryKeyPosition == 1).Name);
        Assert.Contains(decisionColumns, column => column.Name == "source_event_id" && !column.NotNull);
        Assert.Contains(decisionColumns, column => column.Name == "final_report_event_id" && column.NotNull);
        Assert.DoesNotContain(decisionColumns, column => ContainsBodyName(column.Name));
        Assert.DoesNotContain("task_events", await ScalarAsync<string>(connection, "SELECT sql FROM sqlite_master WHERE type='table' AND name='task_review_decisions';"), StringComparison.OrdinalIgnoreCase);

        var gateColumns = await TableColumnsAsync(connection, "task_review_user_gates");
        Assert.Equal(
            ["review_decision_id", "project_id", "task_id", "revision_id", "question_message_id", "user_message_id", "opened_at", "responded_at", "state"],
            gateColumns.Select(column => column.Name));
        Assert.Equal("review_decision_id", gateColumns.Single(column => column.PrimaryKeyPosition == 1).Name);
        Assert.Contains(gateColumns, column => column.Name == "question_message_id" && !column.NotNull);
        Assert.Contains(gateColumns, column => column.Name == "user_message_id" && !column.NotNull);
        Assert.Contains(gateColumns, column => column.Name == "responded_at" && !column.NotNull);
        Assert.DoesNotContain(gateColumns, column => ContainsBodyName(column.Name));
    }

    [Fact]
    public async Task V13_to_v19_adds_typed_review_schema_idempotently_and_preserves_foreign_key_integrity()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await HistoricalMigrationTestDatabase.InitializeThroughAsync(database, 13);

        await database.InitializeAsync();
        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        Assert.Equal(22L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));
        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }

    [Fact]
    public async Task Typed_review_schema_enforces_identity_enum_gate_and_project_ownership_constraints()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var fixture = await CreateFixtureAsync(connection);

        await InsertDecisionAsync(connection, "decision-1", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, "event-1");
        await Assert.ThrowsAsync<SqliteException>(() => InsertDecisionAsync(connection, "decision-2", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, "event-1"));
        await Assert.ThrowsAsync<SqliteException>(() => InsertDecisionAsync(connection, "decision-invalid-outcome", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, null, outcome: "Invalid"));
        await Assert.ThrowsAsync<SqliteException>(() => InsertDecisionAsync(connection, "decision-invalid-action", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, null, actionLevel: "Invalid"));
        await Assert.ThrowsAsync<SqliteException>(() => InsertDecisionAsync(connection, "decision-invalid-authority", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, null, authorityMode: "Invalid"));
        await Assert.ThrowsAsync<SqliteException>(() => InsertDecisionAsync(connection, "decision-invalid-resolution", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, null, authorityResolution: "Invalid"));
        await Assert.ThrowsAsync<SqliteException>(() => InsertDecisionAsync(connection, "decision-task-project-mismatch", fixture.OtherProjectId, fixture.TaskId, fixture.RevisionId, null));
        await Assert.ThrowsAsync<SqliteException>(() => InsertDecisionAsync(connection, "decision-revision-task-mismatch", fixture.ProjectId, fixture.TaskId, fixture.OtherRevisionId, null));

        await InsertGateAsync(connection, "decision-1", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, fixture.QuestionMessageId, null, null, "Open");
        await Assert.ThrowsAsync<SqliteException>(() => InsertGateAsync(connection, "decision-1", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, fixture.QuestionMessageId, null, null, "Open"));
        await Assert.ThrowsAsync<SqliteException>(() => InsertGateAsync(connection, "decision-1", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, fixture.QuestionMessageId, null, null, "Invalid"));

        await InsertDecisionAsync(connection, "decision-2", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, "event-2");
        await Assert.ThrowsAsync<SqliteException>(() => InsertGateAsync(connection, "decision-2", fixture.OtherProjectId, fixture.TaskId, fixture.RevisionId, fixture.QuestionMessageId, null, null, "Open"));
        await Assert.ThrowsAsync<SqliteException>(() => InsertGateAsync(connection, "decision-2", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, fixture.QuestionMessageId, fixture.UserMessageId, null, "Responded"));
        await InsertGateAsync(connection, "decision-2", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, fixture.QuestionMessageId, fixture.UserMessageId, "2026-08-15T00:02:00+00:00", "Responded");

        await ExecuteAsync(connection, "DELETE FROM projects WHERE id = $projectId;", ("$projectId", fixture.ProjectId));
        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM task_review_decisions;"));
        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM task_review_user_gates;"));
        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }

    private static async Task<ReviewFixture> CreateFixtureAsync(SqliteConnection connection)
    {
        const string projectId = "00000000-0000-0000-0000-000000000201";
        const string otherProjectId = "00000000-0000-0000-0000-000000000202";
        const string taskId = "00000000-0000-0000-0000-000000000203";
        const string revisionId = "00000000-0000-0000-0000-000000000204";
        const string otherTaskId = "00000000-0000-0000-0000-000000000205";
        const string otherRevisionId = "00000000-0000-0000-0000-000000000206";
        const string epochId = "00000000-0000-0000-0000-000000000207";
        const string timestamp = "2026-08-15T00:00:00+00:00";

        await ExecuteAsync(connection, "INSERT INTO projects (id,name,root_path,project_type,created_at,last_opened_at) VALUES ($id,'Project','C:/Project',0,$at,$at);", ("$id", projectId), ("$at", timestamp));
        await ExecuteAsync(connection, "INSERT INTO projects (id,name,root_path,project_type,created_at,last_opened_at) VALUES ($id,'Other','C:/Other',0,$at,$at);", ("$id", otherProjectId), ("$at", timestamp));
        await ExecuteAsync(connection, "INSERT INTO tasks (id,project_id,title,status,current_revision_id,created_at,updated_at) VALUES ($id,$project,'Task','Draft',$revision,$at,$at);", ("$id", taskId), ("$project", projectId), ("$revision", revisionId), ("$at", timestamp));
        await ExecuteAsync(connection, "INSERT INTO tasks (id,project_id,title,status,current_revision_id,created_at,updated_at) VALUES ($id,$project,'Other task','Draft',$revision,$at,$at);", ("$id", otherTaskId), ("$project", otherProjectId), ("$revision", otherRevisionId), ("$at", timestamp));
        await InsertRevisionAsync(connection, revisionId, taskId, timestamp);
        await InsertRevisionAsync(connection, otherRevisionId, otherTaskId, timestamp);
        await ExecuteAsync(connection, "INSERT INTO project_leaders (project_id,current_epoch_id,created_at,updated_at) VALUES ($project,NULL,$at,$at);", ("$project", projectId), ("$at", timestamp));
        await ExecuteAsync(connection, "INSERT INTO leader_session_epochs (id,project_id,provider_id,provider_account_id,model_id,agent_session_id,started_at,last_active_at) VALUES ($id,$project,'codex','00000000-0000-0000-0000-000000000208','model','00000000-0000-0000-0000-000000000209',$at,$at);", ("$id", epochId), ("$project", projectId), ("$at", timestamp));
        await ExecuteAsync(connection, "INSERT INTO leader_messages (epoch_id,sequence,role,text,created_at) VALUES ($epoch,1,'user','question body',$at);", ("$epoch", epochId), ("$at", timestamp));
        var questionMessageId = await ScalarAsync<long>(connection, "SELECT last_insert_rowid();");
        await ExecuteAsync(connection, "INSERT INTO leader_messages (epoch_id,sequence,role,text,created_at) VALUES ($epoch,2,'user','response body',$at);", ("$epoch", epochId), ("$at", timestamp));
        var userMessageId = await ScalarAsync<long>(connection, "SELECT last_insert_rowid();");

        return new(projectId, otherProjectId, taskId, revisionId, otherRevisionId, questionMessageId, userMessageId);
    }

    private static Task InsertRevisionAsync(SqliteConnection connection, string revisionId, string taskId, string timestamp) =>
        ExecuteAsync(connection, "INSERT INTO task_revisions (id,task_id,revision_number,goal,scope,out_of_scope,acceptance_json,risk_level,recommended_provider_id,recommended_provider_account_id,recommended_model_profile_id,recommended_agent_runtime_id,change_reason,approved_by,created_at) VALUES ($id,$task,1,'Goal','Scope','None','[]','Low','codex','account','model','runtime','Initial','Leader',$at);", ("$id", revisionId), ("$task", taskId), ("$at", timestamp));

    private static Task InsertDecisionAsync(SqliteConnection connection, string decisionId, string projectId, string taskId, string revisionId, string? sourceEventId, string outcome = "Pass", string actionLevel = "L1LocalFix", string authorityMode = "Balanced", string authorityResolution = "AutoProceed") =>
        ExecuteAsync(connection, "INSERT INTO task_review_decisions (review_decision_id,project_id,task_id,revision_id,source_event_id,final_report_event_id,outcome,action_level,authority_mode,authority_resolution,authority_mode_recording,created_at) VALUES ($id,$project,$task,$revision,$source,$report,$outcome,$action,$authority,$resolution,'Recorded','2026-08-15T00:01:00+00:00');", ("$id", decisionId), ("$project", projectId), ("$task", taskId), ("$revision", revisionId), ("$source", sourceEventId), ("$report", Guid.NewGuid().ToString()), ("$outcome", outcome), ("$action", actionLevel), ("$authority", authorityMode), ("$resolution", authorityResolution));

    private static Task InsertGateAsync(SqliteConnection connection, string decisionId, string projectId, string taskId, string revisionId, long questionMessageId, long? userMessageId, string? respondedAt, string state) =>
        ExecuteAsync(connection, "INSERT INTO task_review_user_gates (review_decision_id,project_id,task_id,revision_id,question_message_id,user_message_id,opened_at,responded_at,state) VALUES ($decision,$project,$task,$revision,$question,$user,'2026-08-15T00:01:00+00:00',$responded,$state);", ("$decision", decisionId), ("$project", projectId), ("$task", taskId), ("$revision", revisionId), ("$question", questionMessageId), ("$user", userMessageId), ("$responded", respondedAt), ("$state", state));

    private static async Task<IReadOnlyList<TableColumn>> TableColumnsAsync(SqliteConnection connection, string table)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<TableColumn>();
        while (await reader.ReadAsync()) columns.Add(new(reader.GetString(1), reader.GetInt64(3) != 0, reader.GetInt64(5)));
        return columns;
    }

    private static bool ContainsBodyName(string columnName) =>
        columnName.Contains("body", StringComparison.OrdinalIgnoreCase) ||
        columnName.Contains("finalreport", StringComparison.OrdinalIgnoreCase) ||
        columnName.Contains("reasoning", StringComparison.OrdinalIgnoreCase) ||
        columnName.Contains("transcript", StringComparison.OrdinalIgnoreCase) ||
        columnName.Contains("summary", StringComparison.OrdinalIgnoreCase);

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

    private sealed record ReviewFixture(string ProjectId, string OtherProjectId, string TaskId, string RevisionId, string OtherRevisionId, long QuestionMessageId, long UserMessageId);
    private sealed record TableColumn(string Name, bool NotNull, long PrimaryKeyPosition);
}
