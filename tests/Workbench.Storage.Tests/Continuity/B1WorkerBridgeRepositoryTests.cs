using Microsoft.Data.Sqlite;
using Workbench.Core.Continuity;
using Workbench.Core.Tasks;
using Workbench.Core.Workers;
using Workbench.Storage.Continuity;

namespace Workbench.Storage.Tests.Continuity;

public sealed class B1WorkerBridgeRepositoryTests
{
    [Fact]
    public async Task Links_task_execution_session_and_evidence_and_roundtrips()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var task = Guid.NewGuid(); var revision = Guid.NewGuid(); var execution = Guid.NewGuid();
        await InsertWorkerRowsAsync(fixture, task, revision, execution);
        var repository = new B1WorkerBridgeRepository(fixture.Database);
        var link = await repository.LinkTaskAsync(new B1WorkerTaskLink(fixture.Primary.ProjectRef, fixture.Primary.AssignmentRef, fixture.Primary.RevisionRef, task, revision, DateTimeOffset.UtcNow));
        Assert.Equal(task, (await repository.GetTaskLinkAsync(fixture.Primary.ProjectRef, task))!.WorkerTaskId);
        var attempt = await new B1RoutingRepository(fixture.Database).CreateAttemptAsync(new CreateAttemptCommand(fixture.Primary.ProjectRef, fixture.Primary.BootstrapPrincipalRef, new AttemptRef(Guid.NewGuid()), fixture.Primary.AssignmentRef, fixture.Primary.RevisionRef, DateTimeOffset.UtcNow));
        var executionLink = await repository.LinkExecutionAsync(new B1WorkerExecutionLink(fixture.Primary.ProjectRef, attempt.AttemptRef, execution, B1WorkerExecutionRelationKind.Initial, DateTimeOffset.UtcNow));
        Assert.Single(await repository.ListExecutionLinksAsync(fixture.Primary.ProjectRef, attempt.AttemptRef));
        var evidence = await repository.RecordEvidenceAsync(new B1WorkerExecutionEvidence(fixture.Primary.ProjectRef, new EvidenceRef("workbench:test/verification"), execution, task, revision, attempt.AttemptRef, "NotVerifiable", "{\"result\":\"NotVerifiable\"}", DateTimeOffset.UtcNow));
        Assert.Equal(evidence, await repository.GetEvidenceAsync(fixture.Primary.ProjectRef, execution));
        Assert.Equal(link, await repository.GetTaskLinkAsync(fixture.Primary.ProjectRef, task));
        Assert.Equal(executionLink, (await repository.ListExecutionLinksAsync(fixture.Primary.ProjectRef, attempt.AttemptRef)).Single());
    }

    [Fact]
    public async Task Rejects_cross_project_execution_link_and_preserves_legacy_rows_unlinked()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var other = await fixture.AddProjectAsync("other");
        var task = Guid.NewGuid(); var revision = Guid.NewGuid(); var execution = Guid.NewGuid();
        await InsertWorkerRowsAsync(fixture, task, revision, execution);
        var attempt = await new B1RoutingRepository(fixture.Database).CreateAttemptAsync(new CreateAttemptCommand(fixture.Primary.ProjectRef, fixture.Primary.BootstrapPrincipalRef, new AttemptRef(Guid.NewGuid()), fixture.Primary.AssignmentRef, fixture.Primary.RevisionRef, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new B1WorkerBridgeRepository(fixture.Database).LinkExecutionAsync(new B1WorkerExecutionLink(other.ProjectRef, attempt.AttemptRef, execution, B1WorkerExecutionRelationKind.Initial, DateTimeOffset.UtcNow)));
        Assert.Null(await new B1WorkerBridgeRepository(fixture.Database).GetTaskLinkAsync(fixture.Primary.ProjectRef, task));
    }

    [Fact]
    public async Task Rejects_execution_from_another_project_even_when_attempt_is_valid()
    {
        await using var fixture = await B1ContinuityStorageFixture.CreateAsync();
        var other = await fixture.AddProjectAsync("other");
        var execution = Guid.NewGuid(); var task = Guid.NewGuid(); var revision = Guid.NewGuid();
        await InsertWorkerRowsAsync(fixture, task, revision, execution, other.ProjectRef.Value);
        var attempt = await new B1RoutingRepository(fixture.Database).CreateAttemptAsync(new CreateAttemptCommand(fixture.Primary.ProjectRef, fixture.Primary.BootstrapPrincipalRef, new AttemptRef(Guid.NewGuid()), fixture.Primary.AssignmentRef, fixture.Primary.RevisionRef, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new B1WorkerBridgeRepository(fixture.Database).LinkExecutionAsync(new B1WorkerExecutionLink(fixture.Primary.ProjectRef, attempt.AttemptRef, execution, B1WorkerExecutionRelationKind.Initial, DateTimeOffset.UtcNow)));
    }

    private static async Task InsertWorkerRowsAsync(B1ContinuityStorageFixture fixture, Guid task, Guid revision, Guid execution, Guid? projectId = null)
    {
        await using var connection = fixture.Database.CreateConnection(); await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO tasks(id,project_id,title,status,current_revision_id,created_at,updated_at) VALUES($task,$project,'Task','ReadyToStart',$revision,$at,$at); INSERT INTO task_revisions(id,task_id,revision_number,goal,scope,out_of_scope,acceptance_json,risk_level,recommended_provider_id,recommended_provider_account_id,recommended_model_profile_id,recommended_agent_runtime_id,change_reason,approved_by,created_at) VALUES($revision,$task,1,'Goal','Scope','Out','[\"A\"]','Low','provider','account','model','runtime','initial','User',$at); INSERT INTO worker_executions(id,project_id,task_id,execution_start_revision_id,execution_start_revision_number,current_ack_revision_id,current_ack_revision_number,base_commit,target_branch,provider_id,provider_account_id,model_profile_id,agent_runtime_id,worker_branch,worker_worktree_path,state,created_at,updated_at) VALUES($execution,$project,$task,$revision,1,$revision,1,'base','master','provider','account','model','runtime','worker','C:/worker','Interrupted',$at,$at);";
        command.Parameters.AddWithValue("$task", task.ToString()); command.Parameters.AddWithValue("$project", (projectId ?? fixture.Primary.ProjectRef.Value).ToString()); command.Parameters.AddWithValue("$revision", revision.ToString()); command.Parameters.AddWithValue("$execution", execution.ToString()); command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }
}
