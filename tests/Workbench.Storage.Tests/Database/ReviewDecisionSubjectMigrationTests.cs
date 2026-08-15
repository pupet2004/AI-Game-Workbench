using Microsoft.Data.Sqlite;
using Workbench.Storage.Database;

namespace Workbench.Storage.Tests.Database;

public sealed class ReviewDecisionSubjectMigrationTests
{
    [Fact]
    public async Task V17_to_v18_backfills_e1_e2a_subjects_and_preserves_responded_gate()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        var fixture = await PrepareV17Async(database);
        var e1 = await AddDecisionAsync(database, fixture, "Pass", "L1LocalFix");
        var e2a = await AddDecisionAsync(database, fixture, "AskUser", "L3DecisionRequired");

        await using (var connection = database.CreateConnection())
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, "INSERT INTO task_review_user_gates(review_decision_id,project_id,task_id,revision_id,question_message_id,user_message_id,opened_at,responded_at,state) VALUES($id,$p,$t,$r,NULL,NULL,$at,$at,'Responded');", ("$id", e2a.DecisionId.ToString()), ("$p", fixture.ProjectId.ToString()), ("$t", fixture.TaskId.ToString()), ("$r", fixture.RevisionId.ToString()), ("$at", fixture.At));
        }

        await database.InitializeAsync();
        await database.InitializeAsync();

        await using var verified = database.CreateConnection(); await verified.OpenAsync();
        Assert.Equal(18L, await ScalarAsync<long>(verified, "PRAGMA user_version;"));
        Assert.Equal(e1.FinalReportId.ToString(), await ScalarAsync<string>(verified, "SELECT final_report_event_id FROM task_review_decisions WHERE review_decision_id=$id;", ("$id", e1.DecisionId.ToString())));
        Assert.Equal(e2a.FinalReportId.ToString(), await ScalarAsync<string>(verified, "SELECT final_report_event_id FROM task_review_decisions WHERE review_decision_id=$id;", ("$id", e2a.DecisionId.ToString())));
        Assert.Equal("Responded", await ScalarAsync<string>(verified, "SELECT state FROM task_review_user_gates WHERE review_decision_id=$id;", ("$id", e2a.DecisionId.ToString())));
        Assert.Equal(0L, await ScalarAsync<long>(verified, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }

    [Theory]
    [InlineData("missing-source")]
    [InlineData("malformed-payload")]
    [InlineData("missing-subject")]
    public async Task V17_to_v18_rejects_unverifiable_subject_without_partial_migration(string failure)
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        var fixture = await PrepareV17Async(database);
        await AddDecisionAsync(database, fixture, "Pass", "L1LocalFix");
        await AddDecisionAsync(database, fixture, "Pass", "L1LocalFix", failure);

        await Assert.ThrowsAsync<DatabaseInitializationException>(() => database.InitializeAsync());

        await using var verified = database.CreateConnection(); await verified.OpenAsync();
        Assert.Equal(17L, await ScalarAsync<long>(verified, "PRAGMA user_version;"));
        Assert.Equal(0L, await ScalarAsync<long>(verified, "SELECT COUNT(*) FROM pragma_table_info('task_review_decisions') WHERE name='final_report_event_id';"));
        Assert.Equal(2L, await ScalarAsync<long>(verified, "SELECT COUNT(*) FROM task_review_decisions;"));
    }

    [Fact]
    public async Task V17_to_v18_rejects_duplicate_subject_atomically()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        var fixture = await PrepareV17Async(database);
        var first = await AddDecisionAsync(database, fixture, "Pass", "L1LocalFix");
        await AddDecisionAsync(database, fixture, "AskUser", "L3DecisionRequired", finalReportId: first.FinalReportId);

        await Assert.ThrowsAsync<DatabaseInitializationException>(() => database.InitializeAsync());
        await using var verified = database.CreateConnection(); await verified.OpenAsync();
        Assert.Equal(17L, await ScalarAsync<long>(verified, "PRAGMA user_version;"));
        Assert.Equal(0L, await ScalarAsync<long>(verified, "SELECT COUNT(*) FROM pragma_table_info('task_review_decisions') WHERE name='final_report_event_id';"));
    }

    private static async Task<Fixture> PrepareV17Async(WorkbenchDatabase database)
    {
        await database.InitializeAsync();
        var fixture = new Fixture(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.ToString("O"));
        await using var connection = database.CreateConnection(); await connection.OpenAsync();
        await ExecuteAsync(connection, "INSERT INTO projects(id,name,root_path,project_type,created_at,last_opened_at) VALUES($p,'P','C:/P',0,$at,$at);", ("$p", fixture.ProjectId.ToString()), ("$at", fixture.At));
        await ExecuteAsync(connection, "INSERT INTO tasks(id,project_id,title,status,current_revision_id,created_at,updated_at) VALUES($t,$p,'T','Draft',$r,$at,$at);", ("$t", fixture.TaskId.ToString()), ("$p", fixture.ProjectId.ToString()), ("$r", fixture.RevisionId.ToString()), ("$at", fixture.At));
        await ExecuteAsync(connection, "INSERT INTO task_revisions(id,task_id,revision_number,goal,scope,out_of_scope,acceptance_json,risk_level,recommended_provider_id,recommended_provider_account_id,recommended_model_profile_id,recommended_agent_runtime_id,change_reason,approved_by,created_at) VALUES($r,$t,1,'G','S','O','[]','Low','p','a','m','rt','initial','leader',$at);", ("$r", fixture.RevisionId.ToString()), ("$t", fixture.TaskId.ToString()), ("$at", fixture.At));
        await ExecuteAsync(connection, "PRAGMA foreign_keys = OFF; DROP TABLE task_review_decisions; CREATE TABLE task_review_decisions (review_decision_id TEXT PRIMARY KEY,project_id TEXT NOT NULL,task_id TEXT NOT NULL,revision_id TEXT NOT NULL,source_event_id TEXT NULL UNIQUE,outcome TEXT NOT NULL,action_level TEXT NOT NULL,authority_mode TEXT NULL,authority_resolution TEXT NOT NULL,authority_mode_recording TEXT NOT NULL,created_at TEXT NOT NULL); CREATE UNIQUE INDEX ux_task_review_decisions_identity ON task_review_decisions(review_decision_id,project_id,task_id,revision_id); PRAGMA user_version = 17; PRAGMA foreign_keys = ON;");
        return fixture;
    }

    private static async Task<Decision> AddDecisionAsync(WorkbenchDatabase database, Fixture fixture, string outcome, string action, string? failure = null, Guid? finalReportId = null)
    {
        var decisionId = Guid.NewGuid(); var reportId = finalReportId ?? Guid.NewGuid();
        await using var connection = database.CreateConnection(); await connection.OpenAsync();
        var source = failure == "missing-source" ? Guid.NewGuid() : decisionId;
        if (failure != "missing-source")
        {
            var payload = failure == "malformed-payload" ? "{" : failure == "missing-subject" ? $"{{\"TaskRevisionId\":\"{fixture.RevisionId}\"}}" : $"{{\"TaskRevisionId\":\"{fixture.RevisionId}\",\"FinalReportEventId\":\"{reportId}\"}}";
            await ExecuteAsync(connection, "INSERT INTO task_events(id,project_id,task_id,execution_id,event_type,payload_json,created_at) VALUES($id,$p,$t,NULL,'LeaderReviewDecisionRecorded',$payload,$at);", ("$id", decisionId.ToString()), ("$p", fixture.ProjectId.ToString()), ("$t", fixture.TaskId.ToString()), ("$payload", payload), ("$at", fixture.At));
        }
        await ExecuteAsync(connection, "INSERT INTO task_review_decisions(review_decision_id,project_id,task_id,revision_id,source_event_id,outcome,action_level,authority_mode,authority_resolution,authority_mode_recording,created_at) VALUES($id,$p,$t,$r,$source,$outcome,$action,'Balanced','AutoProceed','Recorded',$at);", ("$id", decisionId.ToString()), ("$p", fixture.ProjectId.ToString()), ("$t", fixture.TaskId.ToString()), ("$r", fixture.RevisionId.ToString()), ("$source", source.ToString()), ("$outcome", outcome), ("$action", action), ("$at", fixture.At));
        return new(decisionId, reportId);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters) { var command = connection.CreateCommand(); command.CommandText = sql; foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value); await command.ExecuteNonQueryAsync(); }
    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters) { var command = connection.CreateCommand(); command.CommandText = sql; foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value); return (T)(await command.ExecuteScalarAsync())!; }
    private sealed record Fixture(Guid ProjectId, Guid TaskId, Guid RevisionId, string At);
    private sealed record Decision(Guid DecisionId, Guid FinalReportId);
}
