using Microsoft.Data.Sqlite;
using Workbench.Core.Tasks;
using Workbench.Core.Workers;
using Workbench.Storage.Database;
using Workbench.Storage.Workers;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Workers;

public sealed class WorkerExecutionRepositoryTests
{
    [Fact]
    public async Task Roundtrips_all_execution_identity_and_state_fields()
    {
        await using var f = await Fixture.CreateAsync();
        var execution = f.Execution(WorkerExecutionState.WorkspaceCreating);
        await f.Repository.CreateAsync(execution);
        var actual = await f.Repository.GetAsync(f.ProjectId, execution.ExecutionId);
        Assert.NotNull(actual);
        Assert.Equal(execution, actual);
    }

    [Theory]
    [InlineData(WorkerExecutionState.Preparing)]
    [InlineData(WorkerExecutionState.WorkspaceCreating)]
    [InlineData(WorkerExecutionState.WorkspaceCreated)]
    [InlineData(WorkerExecutionState.RuntimeStarting)]
    [InlineData(WorkerExecutionState.Running)]
    [InlineData(WorkerExecutionState.Blocked)]
    [InlineData(WorkerExecutionState.Interrupted)]
    [InlineData(WorkerExecutionState.CompletedPendingReview)]
    [InlineData(WorkerExecutionState.Failed)]
    public async Task Roundtrips_every_closed_state(WorkerExecutionState state)
    {
        await using var f = await Fixture.CreateAsync();
        var execution = f.Execution(state);
        await f.Repository.CreateAsync(execution);
        Assert.Equal(state, (await f.Repository.GetAsync(f.ProjectId, execution.ExecutionId))!.State);
    }

    [Fact]
    public async Task Running_requires_matching_session_and_worktree_identity()
    {
        await using var f = await Fixture.CreateAsync();
        var execution = f.Execution(WorkerExecutionState.RuntimeStarting);
        await f.Repository.CreateAsync(execution);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Repository.UpdateStateAsync(f.ProjectId, execution.TaskId, execution.ExecutionId, WorkerExecutionState.Running));
        await f.Repository.PersistSessionIdentityAsync(f.ProjectId, execution.TaskId, execution.ExecutionId, "agent", null, "C:/P2/main");
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Repository.UpdateStateAsync(f.ProjectId, execution.TaskId, execution.ExecutionId, WorkerExecutionState.Running));
        Assert.Equal(WorkerExecutionState.RuntimeStarting, (await f.Repository.GetAsync(f.ProjectId, execution.ExecutionId))!.State);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TemporaryDatabase _temporary;
        public WorkbenchDatabase Database { get; }
        public WorkerExecutionRepository Repository { get; }
        public Guid ProjectId { get; } = Guid.NewGuid();
        public Guid TaskId { get; } = Guid.NewGuid();
        public Guid RevisionId { get; } = Guid.NewGuid();
        private Fixture(TemporaryDatabase temporary, WorkbenchDatabase database) { _temporary = temporary; Database = database; Repository = new(database); }
        public static async Task<Fixture> CreateAsync()
        {
            var t = new TemporaryDatabase(); var d = new WorkbenchDatabase(t.DatabasePath); await d.InitializeAsync();
            await using var c = d.CreateConnection(); await c.OpenAsync(); var q = c.CreateCommand();
            q.CommandText = $"INSERT INTO projects(id,name,root_path,project_type,created_at,last_opened_at) VALUES('{Guid.NewGuid()}','P','C:/P',0,'2026-01-01T00:00:00+00:00','2026-01-01T00:00:00+00:00');";
            await q.ExecuteNonQueryAsync();
            var f = new Fixture(t, d); await using var c2 = d.CreateConnection(); await c2.OpenAsync(); var s = c2.CreateCommand();
            s.CommandText = $"INSERT INTO projects(id,name,root_path,project_type,created_at,last_opened_at) VALUES('{f.ProjectId}','P2','C:/P2',0,'2026-01-01T00:00:00+00:00','2026-01-01T00:00:00+00:00'); INSERT INTO tasks(id,project_id,title,status,current_revision_id,created_at,updated_at) VALUES('{f.TaskId}','{f.ProjectId}','T','ReadyToStart','{f.RevisionId}','2026-01-01T00:00:00+00:00','2026-01-01T00:00:00+00:00'); INSERT INTO task_revisions(id,task_id,revision_number,goal,scope,out_of_scope,acceptance_json,risk_level,recommended_provider_id,recommended_provider_account_id,recommended_model_profile_id,recommended_agent_runtime_id,change_reason,approved_by,created_at) VALUES('{f.RevisionId}','{f.TaskId}',1,'G','S','O','[\"A\"]','Low','provider','account','model','runtime','initial','User','2026-01-01T00:00:00+00:00');";
            await s.ExecuteNonQueryAsync(); return f;
        }
        public StoredWorkerExecution Execution(WorkerExecutionState state) { var now=DateTimeOffset.UtcNow; var rev=new TaskRevisionReference(TaskId,RevisionId,1); return new(Guid.NewGuid(),ProjectId,TaskId,rev,rev,"base","master",ProviderAccountBinding.Create("provider","account"),ExecutionProfile.Create("provider","account","model","runtime"),"worker","C:/P2/worker",state,state==WorkerExecutionState.Running?"agent":null,state==WorkerExecutionState.Running?"external":null,state==WorkerExecutionState.Running?"C:/P2/worker":null,now,now); }
        public ValueTask DisposeAsync()=>_temporary.DisposeAsync();
    }
}
