using Microsoft.Data.Sqlite;
using Workbench.Core.Tasks;
using Workbench.Core.Workers;
using Workbench.Storage.Database;
using Workbench.Storage.Workers;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Workers;

public sealed class RevisionAckTests
{
    [Fact]
    public async Task Advances_only_when_expected_revision_matches()
    {
        await using var f=await Fixture.CreateAsync();
        Assert.True(await f.Repository.AdvanceAcknowledgedRevisionAsync(f.ProjectId,f.TaskId,f.ExecutionId,1, f.Revision2,2));
        var current=await f.Repository.GetAsync(f.ProjectId,f.ExecutionId);
        Assert.Equal(f.Revision1,current!.ExecutionStartRevision.RevisionId);
        Assert.Equal(f.Revision2,current.CurrentAcknowledgedRevision.RevisionId);
        Assert.False(await f.Repository.AdvanceAcknowledgedRevisionAsync(f.ProjectId,f.TaskId,f.ExecutionId,1,f.Revision3,3));
    }

    [Fact]
    public async Task Rejects_wrong_ownership_cross_task_and_unknown_target()
    {
        await using var f=await Fixture.CreateAsync();
        Assert.False(await f.Repository.AdvanceAcknowledgedRevisionAsync(Guid.NewGuid(),f.TaskId,f.ExecutionId,1,f.Revision2,2));
        Assert.False(await f.Repository.AdvanceAcknowledgedRevisionAsync(f.ProjectId,Guid.NewGuid(),f.ExecutionId,1,f.Revision2,2));
        Assert.False(await f.Repository.AdvanceAcknowledgedRevisionAsync(f.ProjectId,f.TaskId,f.ExecutionId,1,Guid.NewGuid(),2));
        Assert.False(await f.Repository.AdvanceAcknowledgedRevisionAsync(f.ProjectId,f.TaskId,f.ExecutionId,1,f.OtherTaskRevision,1));
    }

    [Fact]
    public async Task Duplicate_ack_is_stale_and_frozen_identity_survives()
    {
        await using var f=await Fixture.CreateAsync(); var before=await f.Repository.GetAsync(f.ProjectId,f.ExecutionId);
        Assert.True(await f.Repository.AdvanceAcknowledgedRevisionAsync(f.ProjectId,f.TaskId,f.ExecutionId,1,f.Revision2,2));
        Assert.False(await f.Repository.AdvanceAcknowledgedRevisionAsync(f.ProjectId,f.TaskId,f.ExecutionId,1,f.Revision2,2));
        var after=await f.Repository.GetAsync(f.ProjectId,f.ExecutionId);
        Assert.Equal(before!.ExecutionStartRevision,after!.ExecutionStartRevision); Assert.Equal(before.BaseCommit,after.BaseCommit); Assert.Equal(before.TargetBranch,after.TargetBranch); Assert.Equal(before.WorkerBranch,after.WorkerBranch); Assert.Equal(before.WorkerWorktreePath,after.WorkerWorktreePath);
    }

    [Fact]
    public async Task Concurrent_ack_attempts_allow_at_most_one_success()
    {
        await using var f=await Fixture.CreateAsync();
        var winner=await Task.WhenAll(
            f.Repository.AdvanceAcknowledgedRevisionAsync(f.ProjectId,f.TaskId,f.ExecutionId,1,f.Revision2,2),
            f.Repository.AdvanceAcknowledgedRevisionAsync(f.ProjectId,f.TaskId,f.ExecutionId,1,f.Revision3,3));
        Assert.Equal(1,winner.Count(x=>x));
    }

    private sealed class Fixture:IAsyncDisposable
    {
        private readonly TemporaryDatabase _t; public WorkbenchDatabase Db{get;} public WorkerExecutionRepository Repository{get;} public Guid ProjectId{get;}=Guid.NewGuid(); public Guid TaskId{get;}=Guid.NewGuid(); public Guid Revision1{get;}=Guid.NewGuid(); public Guid Revision2{get;}=Guid.NewGuid(); public Guid Revision3{get;}=Guid.NewGuid(); public Guid OtherTaskRevision{get;}=Guid.NewGuid(); public Guid ExecutionId{get;}=Guid.NewGuid();
        private Fixture(TemporaryDatabase t,WorkbenchDatabase d){_t=t;Db=d;Repository=new(d);}
        public static async Task<Fixture>CreateAsync(){var t=new TemporaryDatabase();var d=new WorkbenchDatabase(t.DatabasePath);await d.InitializeAsync();var f=new Fixture(t,d);await using var c=d.CreateConnection();await c.OpenAsync();var q=c.CreateCommand();q.CommandText=$"INSERT INTO projects(id,name,root_path,project_type,created_at,last_opened_at) VALUES('{f.ProjectId}','P','C:/P',0,'2026-01-01T00:00:00+00:00','2026-01-01T00:00:00+00:00'); INSERT INTO tasks(id,project_id,title,status,current_revision_id,created_at,updated_at) VALUES('{f.TaskId}','{f.ProjectId}','T','ReadyToStart','{f.Revision1}','2026-01-01T00:00:00+00:00','2026-01-01T00:00:00+00:00');";await q.ExecuteNonQueryAsync();foreach(var (id,n) in new[]{(f.Revision1,1),(f.Revision2,2),(f.Revision3,3)}){var r=d.CreateConnection();await r.OpenAsync();var x=r.CreateCommand();x.CommandText=$"INSERT INTO task_revisions(id,task_id,revision_number,goal,scope,out_of_scope,acceptance_json,risk_level,recommended_provider_id,recommended_provider_account_id,recommended_model_profile_id,recommended_agent_runtime_id,change_reason,approved_by,created_at) VALUES('{id}','{f.TaskId}',{n},'G','S','O','[\\\"A\\\"]','Low','p','a','m','r','c','User','2026-01-01T00:00:00+00:00');";await x.ExecuteNonQueryAsync();await r.DisposeAsync();}var o=Guid.NewGuid();await using var z=d.CreateConnection();await z.OpenAsync();var oq=z.CreateCommand();oq.CommandText=$"INSERT INTO tasks(id,project_id,title,status,current_revision_id,created_at,updated_at) VALUES('{o}','{f.ProjectId}','O','ReadyToStart','{f.OtherTaskRevision}','2026-01-01T00:00:00+00:00','2026-01-01T00:00:00+00:00'); INSERT INTO task_revisions(id,task_id,revision_number,goal,scope,out_of_scope,acceptance_json,risk_level,recommended_provider_id,recommended_provider_account_id,recommended_model_profile_id,recommended_agent_runtime_id,change_reason,approved_by,created_at) VALUES('{f.OtherTaskRevision}','{o}',1,'G','S','O','[\\\"A\\\"]','Low','p','a','m','r','c','User','2026-01-01T00:00:00+00:00');";await oq.ExecuteNonQueryAsync();var e=f.Execution();await f.Repository.CreateAsync(e);return f;}
        private StoredWorkerExecution Execution()=>new(ExecutionId,ProjectId,TaskId,new(TaskId,Revision1,1),new(TaskId,Revision1,1),"base","master",ProviderAccountBinding.Create("p","a"),ExecutionProfile.Create("p","a","m","r"),"branch","C:/P/worker",WorkerExecutionState.Preparing,null,null,null,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
        public ValueTask DisposeAsync()=>_t.DisposeAsync();
    }
}
