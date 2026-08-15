using System.Text.Json;
using Workbench.App.Tests.Support;
using Workbench.App.Worker;
using Workbench.App.Services;
using Workbench.Core.Tasks;
using Workbench.Core.Workers;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.Storage.Workers;
using Workbench.Storage.Tasks;

namespace Workbench.App.Tests.Worker;

public sealed class CanonicalWorkerExecutionCertificationTests
{
    [Fact]
    public async Task C1_create_persists_complete_typed_execution_identity_and_lifecycle()
    {
        await using var fixture = await Fixture.CreateAsync("done");
        var result = await fixture.StartAsync();

        Assert.True(result.Succeeded);
        var execution = await fixture.Repository.GetAsync(fixture.Project.Id, fixture.ExecutionId);
        Assert.NotNull(execution);
        Assert.Equal(fixture.Project.Id, execution.ProjectId);
        Assert.Equal(fixture.TaskId, execution.TaskId);
        Assert.Equal(fixture.Revision.Id, execution.ExecutionStartRevision.RevisionId);
        Assert.Equal("synthetic-base", execution.BaseCommit);
        Assert.Equal("main", execution.TargetBranch);
        Assert.Equal("main", execution.WorkerBranch);
        Assert.Equal(fixture.Project.RootPath, execution.WorkerWorktreePath);
        Assert.Equal(result.WorkerSession!.Id.Value.ToString(), execution.AgentSessionId);
        Assert.Equal(result.WorkerSession.ExternalSessionId, execution.ExternalSessionId);
        Assert.Equal(WorkerExecutionState.CompletedPendingReview, execution.State);
    }

    [Fact]
    public async Task C2_resume_updates_the_same_authority_row_idempotently()
    {
        await using var fixture = await Fixture.CreateAsync("first");
        var first = await fixture.StartAsync();
        fixture.Runtime.QueueTurn(fixture.Completed("second"));
        var second = await fixture.StartAsync(first.WorkerSession!.Id);

        Assert.True(second.Succeeded);
        Assert.Equal(first.WorkerSession.Id, second.WorkerSession!.Id);
        Assert.Equal(2, fixture.Runtime.SentRequests.Count);
        Assert.Single(await fixture.Repository.ListAsync(fixture.Project.Id));
        var execution = (await fixture.Repository.ListAsync(fixture.Project.Id)).Single();
        Assert.Equal(fixture.ExecutionId, execution.ExecutionId);
        Assert.Equal(WorkerExecutionState.Running, execution.State);
    }

    [Fact]
    public async Task C3_restart_recovers_typed_execution_without_provider_or_transcript()
    {
        await using var fixture = await Fixture.CreateAsync("done");
        var result = await fixture.StartAsync();
        await fixture.Services.DisposeAsync();

        var offline = Workbench.App.Services.AppServices.CreateForDatabasePath(fixture.DatabasePath, fixture.Time, new AgentRuntimeRegistry());
        await offline.InitializeAsync();
        try
        {
            var reopened = await offline.ProjectOpenService.OpenAsync(fixture.Project.RootPath);
            Assert.Equal(fixture.Project.Id, reopened.Project.Id);
            var execution = await offline.WorkerExecutionRepository.GetAsync(fixture.Project.Id, fixture.ExecutionId);
            Assert.NotNull(execution);
            Assert.Equal(result.WorkerSession!.Id.Value.ToString(), execution.AgentSessionId);
            Assert.Equal(result.WorkerSession.ExternalSessionId, execution.ExternalSessionId);
            Assert.Equal("synthetic-base", execution.BaseCommit);
            Assert.Equal("main", execution.TargetBranch);
            Assert.Equal(fixture.Project.RootPath, execution.WorkerWorktreePath);
            var sessions = await offline.WorkerRoutingStore.ListSessionsAsync(fixture.Project.Id);
            Assert.Contains(sessions, item => item.ExecutionId == fixture.ExecutionId);
        }
        finally
        {
            await offline.DisposeAsync();
        }
    }

    [Fact]
    public async Task C4_corrupting_worker_events_does_not_destroy_typed_current_recovery()
    {
        await using var fixture = await Fixture.CreateAsync("done");
        var result = await fixture.StartAsync();
        await fixture.DeleteWorkerEventsAsync();

        var store = new TaskEventWorkerRoutingStore(new TaskEventRepository(fixture.Services.Database), fixture.Repository);
        var sessions = await store.ListSessionsAsync(fixture.Project.Id);
        var recovered = Assert.Single(sessions);
        Assert.Equal(fixture.ExecutionId, recovered.ExecutionId);
        Assert.Equal(result.WorkerSession!.Id, recovered.Session.Id);
        Assert.Equal("Completed", recovered.Session.Status.ToString());
    }

    [Fact]
    public async Task C5_event_history_remains_available_as_history_after_typed_write()
    {
        await using var fixture = await Fixture.CreateAsync("done");
        await fixture.StartAsync();
        var events = await new TaskEventRepository(fixture.Services.Database).ListAsync(fixture.Project.Id, fixture.TaskId, 100);
        Assert.Contains(events, item => item.Type == "WorkerSessionStarted");
        Assert.Contains(events, item => item.Type == "WorkerToLeaderHandoff");
    }

    [Fact]
    public async Task Typed_lifecycle_wins_when_a_later_event_payload_is_stale()
    {
        await using var fixture = await Fixture.CreateAsync("done");
        var result = await fixture.StartAsync();
        await new TaskEventRepository(fixture.Services.Database).AppendAsync(new StoredTaskEvent(
            Guid.NewGuid(), fixture.Project.Id, fixture.TaskId, null, "WorkerToLeaderHandoff",
            JsonSerializer.Serialize(new WorkerHandoff(fixture.Project.Id, fixture.TaskId, result.WorkerSession!.Id,
                "Worker", AgentSessionStatus.Running, "stale", fixture.Time.GetUtcNow())), fixture.Time.GetUtcNow()));

        var session = Assert.Single(await fixture.Services.WorkerRoutingStore.ListSessionsAsync(fixture.Project.Id));
        Assert.Equal(AgentSessionStatus.Completed, session.Session.Status);
    }

    [Fact]
    public async Task C6_event_only_legacy_session_is_readable_but_not_promoted_to_typed_authority()
    {
        await using var fixture = await Fixture.CreateAsync(null);
        var legacySession = new AgentSession(AgentSessionId.New(), fixture.Runtime.Account.Id, fixture.Runtime.Provider.Id,
            "model-a", fixture.Project.RootPath, "legacy-external", AgentSessionStatus.Completed, fixture.Time.GetUtcNow(), fixture.Time.GetUtcNow());
        await fixture.Services.WorkerRoutingStore.SaveSessionAsync(new WorkerSessionRecord(
            fixture.Project.Id, fixture.TaskId, "Legacy", legacySession, fixture.Profile, "Legacy", fixture.Time.GetUtcNow()));

        var sessions = await fixture.Services.WorkerRoutingStore.ListSessionsAsync(fixture.Project.Id);
        Assert.Contains(sessions, item => item.Session.Id == legacySession.Id && item.ExecutionId is null);
        Assert.Empty(await fixture.Repository.ListAsync(fixture.Project.Id));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(AppTestContext context, FakeAgentRuntime runtime, Workbench.Core.Projects.Project project, StoredTask task, TaskRevision revision, ExecutionProfile profile)
        {
            Services = context.Services;
            Time = context.Time;
            Runtime = runtime;
            Project = project;
            TaskId = task.TaskId;
            Revision = revision;
            Profile = profile;
            ExecutionId = Guid.NewGuid();
            Identity = WorkerExecutionIdentity.Start(revision.CreateReference(), "synthetic-base", "main",
                ProviderAccountBinding.Create(profile.ProviderId, profile.ProviderAccountId), profile, "main", project.RootPath);
        }

        public AppServices Services { get; }
        public MutableTimeProvider Time { get; }
        public FakeAgentRuntime Runtime { get; }
        public Workbench.Core.Projects.Project Project { get; }
        public Guid TaskId { get; }
        public TaskRevision Revision { get; }
        public ExecutionProfile Profile { get; }
        public Guid ExecutionId { get; }
        public WorkerExecutionIdentity Identity { get; }
        public WorkerExecutionRepository Repository => Services.WorkerExecutionRepository;
        public string DatabasePath => Services.Database.DatabasePath;

        public static async Task<Fixture> CreateAsync(string? response)
        {
            var runtime = new FakeAgentRuntime();
            var registry = new AgentRuntimeRegistry();
            registry.Register(runtime);
            var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
            var projectPath = Path.GetFullPath(Path.Combine(context.DatabasePath, "..", "project"));
            Directory.CreateDirectory(projectPath);
            var project = (await context.Services.ProjectOpenService.OpenAsync(projectPath)).Project;
            var profile = ExecutionProfile.Create(runtime.Provider.Id.Value, runtime.Account.Id.Value.ToString(), "model-a", "runtime");
            var revision = new TaskRevision(Guid.NewGuid(), 1, "goal", "scope", "out", ["accept"], TaskRiskLevel.Low,
                profile, "initial", TaskRevisionApprover.User, context.Time.GetUtcNow(), null);
            await context.Services.TaskRepository.CreateAsync(project.Id, new TaskDraft(revision.TaskId, "Task", revision.Goal,
                revision.Scope, revision.OutOfScope, revision.Acceptance, revision.RiskLevel, profile, context.Time.GetUtcNow(), revision));
            var fixture = new Fixture(context, runtime, project, (await context.Services.TaskRepository.GetAsync(project.Id, revision.TaskId))!, revision, profile);
            if (response is not null) runtime.QueueTurn(fixture.Completed(response));
            return fixture;
        }

        public AgentTurnCompleted Completed(string text) => new(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
            JsonSerializer.Serialize(new { Kind = "FinalReport", Message = text, ValidationSummary = "ok" }), null), Time.GetUtcNow());

        public Task<WorkerStartResult> StartAsync(AgentSessionId? reuse = null) => Services.WorkerSessionRouter.StartAsync(new WorkerStartRequest(
            Project, TaskId, Revision.Id, "Task", Profile, "work", reuse, "Worker", ExecutionId: ExecutionId, ExecutionIdentity: Identity));

        public async Task DeleteWorkerEventsAsync()
        {
            await using var connection = Services.Database.CreateConnection();
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM task_events WHERE project_id=$project AND task_id=$task AND event_type IN ('WorkerSessionStarted','WorkerToLeaderHandoff','WorkerRemoved');";
            command.Parameters.AddWithValue("$project", Project.Id.ToString());
            command.Parameters.AddWithValue("$task", TaskId.ToString());
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
        }
    }
}
