using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Tests.Database;

public sealed class ReviewGateReferenceDecouplingMigrationTests
{
    [Fact]
    public async Task Fresh_database_reaches_v19_with_nullable_set_null_message_locators()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();

        Assert.Equal(19L, await ScalarAsync<long>(connection, "PRAGMA user_version;"));
        var columns = await ColumnsAsync(connection);
        Assert.Contains(columns, column => column.Name == "question_message_id" && !column.NotNull);
        Assert.Contains(columns, column => column.Name == "user_message_id" && !column.NotNull);

        var foreignKeys = await ForeignKeysAsync(connection);
        Assert.Contains(foreignKeys, key => key.From == "question_message_id" && key.Table == "leader_messages" && key.To == "id" && key.OnDelete == "SET NULL");
        Assert.Contains(foreignKeys, key => key.From == "user_message_id" && key.Table == "leader_messages" && key.To == "id" && key.OnDelete == "SET NULL");
    }

    [Fact]
    public async Task V14_to_v18_rejects_unverifiable_typed_decisions()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await HistoricalMigrationTestDatabase.InitializeThroughAsync(database, 14);

        await using (var connection = database.CreateConnection())
        {
            await connection.OpenAsync();
            var fixture = await CreateFixtureAsync(connection);
            await InsertV14DecisionAsync(connection, "open", fixture.ProjectId, fixture.TaskId, fixture.RevisionId);
            await InsertGateAsync(connection, "open", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, fixture.QuestionMessageId, null, null, "Open");
            await InsertV14DecisionAsync(connection, "responded", fixture.ProjectId, fixture.TaskId, fixture.RevisionId);
            await InsertGateAsync(connection, "responded", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, fixture.QuestionMessageId, fixture.UserMessageId, fixture.RespondedAt, "Responded");
        }

        await Assert.ThrowsAsync<DatabaseInitializationException>(() => database.InitializeAsync());

        await using var verified = database.CreateConnection();
        await verified.OpenAsync();
        Assert.Equal(17L, await ScalarAsync<long>(verified, "PRAGMA user_version;"));
        Assert.Equal(("Open", (long?)1, (long?)null, (string?)null), await ReadGateAsync(verified, "open"));
        Assert.Equal(("Responded", (long?)1, (long?)2, "2026-08-15T00:02:00+00:00"), await ReadGateAsync(verified, "responded"));
        Assert.Equal(0L, await ScalarAsync<long>(verified, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }

    [Fact]
    public async Task V15_source_deletion_nulls_locators_without_changing_responded_gate_state()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var fixture = await CreateFixtureAsync(connection);
        await InsertDecisionAsync(connection, "responded", fixture.ProjectId, fixture.TaskId, fixture.RevisionId);
        await InsertGateAsync(connection, "responded", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, fixture.QuestionMessageId, fixture.UserMessageId, fixture.RespondedAt, "Responded");

        await ExecuteAsync(connection, "DELETE FROM leader_messages WHERE id = $id;", ("$id", fixture.QuestionMessageId));
        Assert.Equal(("Responded", (long?)null, (long?)2, fixture.RespondedAt), await ReadGateAsync(connection, "responded"));

        await ExecuteAsync(connection, "DELETE FROM leader_messages WHERE id = $id;", ("$id", fixture.UserMessageId));
        Assert.Equal(("Responded", (long?)null, (long?)null, fixture.RespondedAt), await ReadGateAsync(connection, "responded"));
        Assert.Equal(0L, await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }

    [Fact]
    public async Task V15_gate_check_keeps_state_authoritative_when_message_locators_are_missing()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();

        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var fixture = await CreateFixtureAsync(connection);

        await InsertDecisionAsync(connection, "open-null-question", fixture.ProjectId, fixture.TaskId, fixture.RevisionId);
        await InsertGateAsync(connection, "open-null-question", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, null, null, null, "Open");
        await InsertDecisionAsync(connection, "responded-null-locators", fixture.ProjectId, fixture.TaskId, fixture.RevisionId);
        await InsertGateAsync(connection, "responded-null-locators", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, null, null, fixture.RespondedAt, "Responded");

        await InsertDecisionAsync(connection, "invalid-open-user", fixture.ProjectId, fixture.TaskId, fixture.RevisionId);
        await Assert.ThrowsAsync<SqliteException>(() => InsertGateAsync(connection, "invalid-open-user", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, null, fixture.UserMessageId, null, "Open"));
        await InsertDecisionAsync(connection, "invalid-open-time", fixture.ProjectId, fixture.TaskId, fixture.RevisionId);
        await Assert.ThrowsAsync<SqliteException>(() => InsertGateAsync(connection, "invalid-open-time", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, null, null, fixture.RespondedAt, "Open"));
        await InsertDecisionAsync(connection, "invalid-responded-time", fixture.ProjectId, fixture.TaskId, fixture.RevisionId);
        await Assert.ThrowsAsync<SqliteException>(() => InsertGateAsync(connection, "invalid-responded-time", fixture.ProjectId, fixture.TaskId, fixture.RevisionId, null, null, null, "Responded"));
    }

    private static async Task<Fixture> CreateFixtureAsync(SqliteConnection connection)
    {
        const string timestamp = "2026-08-15T00:00:00+00:00";
        await ExecuteAsync(connection, "INSERT INTO projects (id,name,root_path,project_type,created_at,last_opened_at) VALUES ('p','P','C:/P',0,$at,$at);", ("$at", timestamp));
        await ExecuteAsync(connection, "INSERT INTO tasks (id,project_id,title,status,current_revision_id,created_at,updated_at) VALUES ('t','p','T','Draft','r',$at,$at);", ("$at", timestamp));
        await ExecuteAsync(connection, "INSERT INTO task_revisions (id,task_id,revision_number,goal,scope,out_of_scope,acceptance_json,risk_level,recommended_provider_id,recommended_provider_account_id,recommended_model_profile_id,recommended_agent_runtime_id,change_reason,approved_by,created_at) VALUES ('r','t',1,'G','S','O','[]','Low','p','a','m','rt','initial','leader',$at);", ("$at", timestamp));
        await ExecuteAsync(connection, "INSERT INTO project_leaders (project_id,current_epoch_id,created_at,updated_at) VALUES ('p',NULL,$at,$at);", ("$at", timestamp));
        await ExecuteAsync(connection, "INSERT INTO leader_session_epochs (id,project_id,provider_id,provider_account_id,model_id,agent_session_id,started_at,last_active_at) VALUES ('e','p','codex','00000000-0000-0000-0000-000000000001','m','00000000-0000-0000-0000-000000000002',$at,$at);", ("$at", timestamp));
        await ExecuteAsync(connection, "INSERT INTO leader_messages (epoch_id,sequence,role,text,created_at) VALUES ('e',1,'user','question',$at);", ("$at", timestamp));
        var question = await ScalarAsync<long>(connection, "SELECT last_insert_rowid();");
        await ExecuteAsync(connection, "INSERT INTO leader_messages (epoch_id,sequence,role,text,created_at) VALUES ('e',2,'user','answer',$at);", ("$at", timestamp));
        var user = await ScalarAsync<long>(connection, "SELECT last_insert_rowid();");
        return new("p", "t", "r", question, user, "2026-08-15T00:02:00+00:00");
    }

    private static Task InsertDecisionAsync(SqliteConnection connection, string id, string projectId, string taskId, string revisionId) =>
        ExecuteAsync(connection, "INSERT INTO task_review_decisions (review_decision_id,project_id,task_id,revision_id,final_report_event_id,outcome,action_level,authority_mode,authority_resolution,authority_mode_recording,created_at) VALUES ($id,$project,$task,$revision,$report,'AskUser','L3DecisionRequired','Balanced','AskUser','Recorded','2026-08-15T00:01:00+00:00');", ("$id", id), ("$project", projectId), ("$task", taskId), ("$revision", revisionId), ("$report", Guid.NewGuid().ToString()));

    private static Task InsertV14DecisionAsync(SqliteConnection connection, string id, string projectId, string taskId, string revisionId) =>
        ExecuteAsync(connection, "INSERT INTO task_review_decisions (review_decision_id,project_id,task_id,revision_id,outcome,action_level,authority_mode,authority_resolution,created_at) VALUES ($id,$project,$task,$revision,'AskUser','L3DecisionRequired','Balanced','AskUser','2026-08-15T00:01:00+00:00');", ("$id", id), ("$project", projectId), ("$task", taskId), ("$revision", revisionId));

    private static Task InsertGateAsync(SqliteConnection connection, string decisionId, string projectId, string taskId, string revisionId, long? questionMessageId, long? userMessageId, string? respondedAt, string state) =>
        ExecuteAsync(connection, "INSERT INTO task_review_user_gates (review_decision_id,project_id,task_id,revision_id,question_message_id,user_message_id,opened_at,responded_at,state) VALUES ($id,$project,$task,$revision,$question,$user,'2026-08-15T00:01:00+00:00',$responded,$state);", ("$id", decisionId), ("$project", projectId), ("$task", taskId), ("$revision", revisionId), ("$question", questionMessageId), ("$user", userMessageId), ("$responded", respondedAt), ("$state", state));

    private static async Task<(string State, long? QuestionMessageId, long? UserMessageId, string? RespondedAt)> ReadGateAsync(SqliteConnection connection, string decisionId)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT state,question_message_id,user_message_id,responded_at FROM task_review_user_gates WHERE review_decision_id = $id;";
        command.Parameters.AddWithValue("$id", decisionId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetInt64(2), reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static async Task<IReadOnlyList<Column>> ColumnsAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(task_review_user_gates);";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<Column>();
        while (await reader.ReadAsync()) columns.Add(new(reader.GetString(1), reader.GetInt64(3) != 0));
        return columns;
    }

    private static async Task<IReadOnlyList<ForeignKey>> ForeignKeysAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_list(task_review_user_gates);";
        await using var reader = await command.ExecuteReaderAsync();
        var keys = new List<ForeignKey>();
        while (await reader.ReadAsync()) keys.Add(new(reader.GetString(3), reader.GetString(2), reader.GetString(4), reader.GetString(6)));
        return keys;
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

    private sealed record Fixture(string ProjectId, string TaskId, string RevisionId, long QuestionMessageId, long UserMessageId, string RespondedAt);
    private sealed record Column(string Name, bool NotNull);
    private sealed record ForeignKey(string From, string Table, string To, string OnDelete);
}
