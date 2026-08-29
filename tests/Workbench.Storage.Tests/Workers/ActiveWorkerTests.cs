using Microsoft.Data.Sqlite;
using Workbench.Core.Tasks;
using Workbench.Core.Workers;
using Workbench.Storage.Database;
using Workbench.Storage.Tests.Database;
using Workbench.Storage.Workers;

namespace Workbench.Storage.Tests.Workers;

public sealed class ActiveWorkerTests
{
    [Theory]
    [InlineData(WorkerExecutionState.Preparing, WorkerExecutionState.Preparing)]
    [InlineData(WorkerExecutionState.Running, WorkerExecutionState.Preparing)]
    [InlineData(WorkerExecutionState.Blocked, WorkerExecutionState.WorkspaceCreating)]
    public async Task Same_project_can_host_multiple_active_worker_executions(WorkerExecutionState first, WorkerExecutionState second)
    {
        await using var f=await Fixture.CreateAsync(); await f.Repository.CreateAsync(f.Execution(first,1));
        await f.Repository.CreateAsync(f.Execution(second,2));

        var executions = await f.Repository.ListAsync(f.ProjectId);
        Assert.Equal(2, executions.Count);
    }

    [Fact]
    public async Task Different_projects_can_each_have_one_active_execution()
    {
        await using var f=await Fixture.CreateAsync(); await f.Repository.CreateAsync(f.Execution(WorkerExecutionState.Running,1)); await f.Repository.CreateAsync(f.Execution(WorkerExecutionState.Running,2,true));
    }

    [Theory]
    [InlineData(WorkerExecutionState.CompletedPendingReview)]
    [InlineData(WorkerExecutionState.Failed)]
    public async Task Terminal_execution_releases_project_slot(WorkerExecutionState terminal)
    {
        await using var f=await Fixture.CreateAsync(); await f.Repository.CreateAsync(f.Execution(terminal,1)); await f.Repository.CreateAsync(f.Execution(WorkerExecutionState.Preparing,2));
    }

    [Fact]
    public async Task Interrupted_execution_releases_project_slot_for_a_new_attempt()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Repository.CreateAsync(f.Execution(WorkerExecutionState.Interrupted, 1));
        await f.Repository.CreateAsync(f.Execution(WorkerExecutionState.Preparing, 2));
    }

    [Fact]
    public async Task Terminal_execution_can_transition_back_to_active_while_another_worker_runs()
    {
        await using var f=await Fixture.CreateAsync(); var active=f.Execution(WorkerExecutionState.Running,1); var completed=f.Execution(WorkerExecutionState.CompletedPendingReview,2); await f.Repository.CreateAsync(active); await f.Repository.CreateAsync(completed);
        await f.Repository.UpdateStateAsync(f.ProjectId,completed.TaskId,completed.ExecutionId,WorkerExecutionState.Preparing);
        Assert.Equal(WorkerExecutionState.Preparing,(await f.Repository.GetAsync(f.ProjectId,completed.ExecutionId))!.State);
    }

    [Fact]
    public async Task Concurrent_active_inserts_allow_both_workers()
    {
        for (var i=0;i<10;i++)
        {
            await using var f=await Fixture.CreateAsync(); var a=f.Execution(WorkerExecutionState.Preparing,1); var b=f.Execution(WorkerExecutionState.Preparing,2);
            var results=await Task.WhenAll(TryCreateAsync(f.Database,a),TryCreateAsync(f.Database,b));
            Assert.Equal(2,results.Count(x=>x));
        }
    }

    private static async Task<bool> TryCreateAsync(WorkbenchDatabase db,StoredWorkerExecution e){try{await new WorkerExecutionRepository(db).CreateAsync(e);return true;}catch(SqliteException){return false;}}

    private sealed class Fixture:IAsyncDisposable
    {
        private readonly TemporaryDatabase _t; public WorkbenchDatabase Database{get;} public WorkerExecutionRepository Repository{get;} public Guid ProjectId{get;}=Guid.NewGuid(); public Guid Project2Id{get;}=Guid.NewGuid(); public Guid RevisionId{get;}=Guid.NewGuid();
        private Fixture(TemporaryDatabase t,WorkbenchDatabase d){_t=t;Database=d;Repository=new(d);}
        public static async Task<Fixture>CreateAsync(){var t=new TemporaryDatabase();var d=new WorkbenchDatabase(t.DatabasePath);await d.InitializeAsync();var f=new Fixture(t,d);await using var c=d.CreateConnection();await c.OpenAsync();var q=c.CreateCommand();q.CommandText=$"INSERT INTO projects(id,name,root_path,project_type,created_at,last_opened_at) VALUES('{f.ProjectId}','P','C:/P',0,'2026-01-01T00:00:00+00:00','2026-01-01T00:00:00+00:00'),('{f.Project2Id}','P2','C:/P2',0,'2026-01-01T00:00:00+00:00','2026-01-01T00:00:00+00:00');";await q.ExecuteNonQueryAsync();return f;}
        public StoredWorkerExecution Execution(WorkerExecutionState state,int n,bool project2=false){var project=project2?Project2Id:ProjectId;var task=Guid.NewGuid();var rev=Guid.NewGuid();using var c=Database.CreateConnection();c.Open();var q=c.CreateCommand();q.CommandText=$"INSERT INTO tasks(id,project_id,title,status,current_revision_id,created_at,updated_at) VALUES('{task}','{project}','t','ReadyToStart','{rev}','2026-01-01T00:00:00+00:00','2026-01-01T00:00:00+00:00'); INSERT INTO task_revisions(id,task_id,revision_number,goal,scope,out_of_scope,acceptance_json,risk_level,recommended_provider_id,recommended_provider_account_id,recommended_model_profile_id,recommended_agent_runtime_id,change_reason,approved_by,created_at) VALUES('{rev}','{task}',1,'g','s','o','[\\\"a\\\"]','Low','p','a','m','r','c','User','2026-01-01T00:00:00+00:00');";q.ExecuteNonQuery();var now=DateTimeOffset.UtcNow;var rr=new TaskRevisionReference(task,rev,1);return new(Guid.NewGuid(),project,task,rr,rr,"base","master",ProviderAccountBinding.Create("p","a"),ExecutionProfile.Create("p","a","m","r"),$"branch-{n}",$"C:/P{n}/worker",state,state==WorkerExecutionState.Running?"agent":null,state==WorkerExecutionState.Running?"external":null,state==WorkerExecutionState.Running?$"C:/P{n}/worker":null,now,now);}
        public ValueTask DisposeAsync()=>_t.DisposeAsync();
    }
}
