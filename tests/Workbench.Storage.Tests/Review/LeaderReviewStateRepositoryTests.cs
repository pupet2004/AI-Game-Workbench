using Microsoft.Data.Sqlite;
using Workbench.Core.Leaders;
using Workbench.Storage.Database;
using Workbench.Storage.Reviews;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Review;

public sealed class LeaderReviewStateRepositoryTests
{
    [Fact]
    public async Task Live_decision_is_recorded_and_replay_is_idempotent()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var repository = new LeaderReviewStateRepository(database);
        var ids = await SeedAsync(database);
        var request = new LeaderReviewDecisionWriteRequest(ids.DecisionId, ids.ProjectId, ids.TaskId, ids.RevisionId, ids.FinalReportId, "Pass", "L1LocalFix", "AutoProceed", LeaderAuthorityMode.Balanced, ids.Now);

        Assert.Equal(LeaderReviewWriteResult.Applied, await repository.InsertDecisionIfAbsentAsync(request));
        Assert.Equal(LeaderReviewWriteResult.Existing, await repository.InsertDecisionIfAbsentAsync(request));
        Assert.Equal("Recorded", (await repository.GetDecisionAsync(ids.ProjectId, ids.TaskId, ids.DecisionId))!.AuthorityModeRecording);
        Assert.Equal(ids.DecisionId, (await repository.GetDecisionByFinalReportAsync(ids.ProjectId, ids.TaskId, ids.FinalReportId))!.ReviewDecisionId);
        Assert.Null(await repository.GetDecisionByFinalReportAsync(Guid.NewGuid(), ids.TaskId, ids.FinalReportId));
        Assert.Null(await repository.GetDecisionByFinalReportAsync(ids.ProjectId, ids.TaskId, Guid.NewGuid()));
        Assert.Equal(LeaderReviewWriteResult.Conflict, await repository.InsertDecisionIfAbsentAsync(request with { FinalReportEventId = Guid.NewGuid() }));
        Assert.Equal(LeaderReviewWriteResult.Conflict, await repository.InsertDecisionIfAbsentAsync(request with { ReviewDecisionId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.InsertDecisionIfAbsentAsync(new(ids.DecisionId, ids.ProjectId, ids.TaskId, ids.RevisionId, Guid.Empty, "Pass", "L1LocalFix", "AutoProceed", LeaderAuthorityMode.Balanced, ids.Now)));
    }

    [Fact]
    public async Task Gate_cas_allows_first_reply_only_and_returns_all_open_gates()
    {
        await using var temporary = new TemporaryDatabase();
        var database = new WorkbenchDatabase(temporary.DatabasePath);
        await database.InitializeAsync();
        var repository = new LeaderReviewStateRepository(database);
        var ids = await SeedAsync(database);
        await repository.InsertDecisionIfAbsentAsync(new(ids.DecisionId, ids.ProjectId, ids.TaskId, ids.RevisionId, ids.FinalReportId, "AskUser", "L3DecisionRequired", "AskUser", LeaderAuthorityMode.Balanced, ids.Now));
        await repository.OpenUserGateIfAbsentAsync(new(ids.DecisionId, ids.ProjectId, ids.TaskId, ids.RevisionId, null, ids.Now));

        Assert.Single(await repository.GetOpenUserGatesAsync(ids.ProjectId));
        Assert.True(await repository.TryBindFirstUserResponseAsync(ids.ProjectId, ids.TaskId, ids.DecisionId, 101, ids.Now.AddMinutes(1)));
        Assert.False(await repository.TryBindFirstUserResponseAsync(ids.ProjectId, ids.TaskId, ids.DecisionId, 102, ids.Now.AddMinutes(2)));
        Assert.Equal(101L, await repository.GetBoundUserMessageIdAsync(ids.ProjectId, ids.TaskId, ids.DecisionId));
    }

    private static async Task<Ids> SeedAsync(WorkbenchDatabase database)
    {
        var ids = new Ids(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        await ExecuteAsync(connection, "INSERT INTO projects(id,name,root_path,project_type,created_at,last_opened_at) VALUES($p,'P','C:/P',0,$a,$a);", ("$p", ids.ProjectId.ToString()), ("$a", ids.Now.ToString("O")));
        await ExecuteAsync(connection, "INSERT INTO tasks(id,project_id,title,status,current_revision_id,created_at,updated_at) VALUES($t,$p,'T','Draft',$r,$a,$a);", ("$t", ids.TaskId.ToString()), ("$p", ids.ProjectId.ToString()), ("$r", ids.RevisionId.ToString()), ("$a", ids.Now.ToString("O")));
        await ExecuteAsync(connection, "INSERT INTO task_revisions(id,task_id,revision_number,goal,scope,out_of_scope,acceptance_json,risk_level,recommended_provider_id,recommended_provider_account_id,recommended_model_profile_id,recommended_agent_runtime_id,change_reason,approved_by,created_at) VALUES($r,$t,1,'G','S','O','[]','Low','p','a','m','rt','initial','leader',$a);", ("$r", ids.RevisionId.ToString()), ("$t", ids.TaskId.ToString()), ("$a", ids.Now.ToString("O")));
        await ExecuteAsync(connection, "INSERT INTO project_leaders(project_id,current_epoch_id,created_at,updated_at) VALUES($p,NULL,$a,$a);", ("$p", ids.ProjectId.ToString()), ("$a", ids.Now.ToString("O")));
        await ExecuteAsync(connection, "INSERT INTO leader_session_epochs(id,project_id,provider_id,provider_account_id,model_id,agent_session_id,started_at,last_active_at) VALUES($e,$p,'provider','account','model','session',$a,$a);", ("$e", ids.EpochId.ToString()), ("$p", ids.ProjectId.ToString()), ("$a", ids.Now.ToString("O")));
        await ExecuteAsync(connection, "UPDATE project_leaders SET current_epoch_id=$e WHERE project_id=$p;", ("$e", ids.EpochId.ToString()), ("$p", ids.ProjectId.ToString()));
        await ExecuteAsync(connection, "INSERT INTO leader_messages(id,epoch_id,sequence,role,text,created_at) VALUES(101,$e,1,'user','first',$a),(102,$e,2,'user','second',$a);", ("$e", ids.EpochId.ToString()), ("$a", ids.Now.ToString("O")));
        return ids;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record Ids(Guid ProjectId, Guid TaskId, Guid RevisionId, Guid DecisionId, DateTimeOffset Now)
    {
        public Guid EpochId { get; } = Guid.NewGuid();
        public Guid FinalReportId { get; } = Guid.NewGuid();
    }
}
