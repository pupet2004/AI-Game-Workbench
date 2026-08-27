using Workbench.App.Tests.Support;
using Workbench.App.Worker;
using Workbench.Core.Tasks;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.Storage.Tasks;
using Workbench.Storage.Workers;
using Workbench.Storage.Database;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Workbench.App.Tests.Worker;

public sealed class WorkerSessionRoutingTests
{
    [Fact]
    public async Task Confirming_a_draft_creates_one_worker_session_sends_the_leader_prompt_and_persists_the_relation()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, FinalReport("Worker result A"), null), DateTimeOffset.UtcNow));

        var result = await fixture.Router.StartAsync(fixture.NewRequest("Leader prompt A"));

        Assert.True(result.Succeeded);
        Assert.Single(fixture.Runtime.CreateRequests);
        Assert.Equal(fixture.Project.RootPath, fixture.Runtime.CreateRequests[0].WorkingDirectory);
        Assert.Equal(fixture.Profile.ModelProfileId, fixture.Runtime.CreateRequests[0].ModelId);
        Assert.Equal(result.WorkerSession!.Id, fixture.Runtime.SentSessions.Single().Id);
        var sent = fixture.Runtime.SentRequests.Single().Text;
        Assert.StartsWith("Leader prompt A", sent, StringComparison.Ordinal);
        Assert.Contains("# Workbench Worker", sent, StringComparison.Ordinal);
        var events = await fixture.ListAsync();
        Assert.Contains(events, item => item.Type == "WorkerSessionStarted");
        Assert.Contains(events, item => item.Type == "WorkerToLeaderHandoff" && item.Payload.Contains("Worker result A", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Background_worker_start_returns_after_session_is_ready()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Runtime.PauseBeforeEvents = true;
        fixture.Runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, FinalReport("Worker result"), null), DateTimeOffset.UtcNow));

        var result = await fixture.Router.StartAsync(fixture.NewRequest("Leader prompt", waitForCompletion: false));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.WorkerSession);
        await fixture.Runtime.WaitForSendAsync();
        fixture.Runtime.ReleaseSend();
    }

    [Fact]
    public async Task Explicit_reuse_routes_follow_up_to_the_same_worker_without_creating_a_replacement()
    {
        await using var fixture = await Fixture.CreateAsync();
        var worker = fixture.CreateExistingWorker();
        await fixture.Events.SaveSessionAsync(new WorkerSessionRecord(fixture.Project.Id, fixture.Task.TaskId, fixture.Task.Title, worker, fixture.Profile, "Worker 1", DateTimeOffset.UtcNow));
        fixture.Runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(worker.Id, AgentSessionStatus.Completed, FinalReport("Worker result B"), null), DateTimeOffset.UtcNow));

        var result = await fixture.Router.StartAsync(fixture.NewRequest("Leader correction B", worker.Id));

        Assert.True(result.Succeeded);
        Assert.Empty(fixture.Runtime.CreateRequests);
        Assert.Equal(worker.Id, fixture.Runtime.SentSessions.Single().Id);
        var sent = fixture.Runtime.SentRequests.Single().Text;
        Assert.StartsWith("Leader correction B", sent, StringComparison.Ordinal);
        Assert.Contains("# Workbench Worker", sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leader_handoff_delivery_failure_does_not_remove_the_persisted_worker_handoff()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, FinalReport("Worker result"), null), DateTimeOffset.UtcNow));

        var result = await fixture.Router.StartAsync(fixture.NewRequest("Leader prompt", onHandoff: (_, _) => Task.FromException(new InvalidOperationException("Leader unavailable"))));

        Assert.True(result.Succeeded);
        Assert.Contains(await fixture.ListAsync(), item => item.Type == "WorkerToLeaderHandoff" && item.Payload.Contains("Worker result", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Runtime_creation_followed_by_relation_persistence_failure_stops_the_new_session()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Events.FailAppend = true;

        var result = await fixture.Router.StartAsync(fixture.NewRequest("Leader prompt"));

        Assert.False(result.Succeeded);
        Assert.Single(fixture.Runtime.CreatedSessions);
        Assert.Equal(fixture.Runtime.CreatedSessions.Single().Id, fixture.Runtime.StoppedSessions.Single().Id);
    }

    [Fact]
    public async Task Leader_worker_follow_up_roundtrip_reuses_the_same_persisted_session()
    {
        await using var fixture = await Fixture.CreateAsync();
        var router = new WorkerSessionRouter(fixture.Registry, new TaskEventWorkerRoutingStore(new TaskEventRepository(fixture.Database)), TimeProvider.System);
        fixture.Runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, FinalReport("Result A"), null), DateTimeOffset.UtcNow));
        fixture.Runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, FinalReport("Result B"), null), DateTimeOffset.UtcNow));

        var first = await router.StartAsync(fixture.NewRequest("Prompt A"));
        var second = await router.StartAsync(fixture.NewRequest("Correction B", first.WorkerSession!.Id));

        Assert.True(second.Succeeded);
        Assert.Equal(first.WorkerSession.Id, second.WorkerSession!.Id);
        Assert.Single(fixture.Runtime.CreateRequests);
        var prompts = fixture.Runtime.SentRequests.Select(item => item.Text).ToArray();
        Assert.Equal(2, prompts.Length);
        Assert.StartsWith("Prompt A", prompts[0], StringComparison.Ordinal);
        Assert.StartsWith("Correction B", prompts[1], StringComparison.Ordinal);
        Assert.All(prompts, prompt => Assert.Contains("# Workbench Worker", prompt, StringComparison.Ordinal));
        var events = await fixture.ListAsync();
        Assert.Equal(2, events.Count(item => item.Type == "WorkerToLeaderHandoff"));
    }

    [Fact]
    public async Task Explicit_final_report_atomically_moves_only_its_working_assignment_to_reviewing()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.True(await new TaskRepository(fixture.Database).UpdateStatusAsync(fixture.Project.Id, fixture.Task.TaskId,
            TaskLifecycleStatus.Draft, TaskLifecycleStatus.ReadyToStart));
        var eventRepository = new TaskEventRepository(fixture.Database);
        var router = new WorkerSessionRouter(fixture.Registry, new TaskEventWorkerRoutingStore(eventRepository), TimeProvider.System,
            new AssignmentReviewStateRepository(fixture.Database));
        fixture.Runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
            FinalReport("delivered"), null), DateTimeOffset.UtcNow));

        var result = await router.StartAsync(fixture.NewRequest("prompt"));

        Assert.True(result.Succeeded);
        var state = (await new AssignmentReviewStateRepository(fixture.Database)
            .GetRecoveryStateAsync(fixture.Project.Id, fixture.Task.TaskId))!;
        Assert.Equal(TaskLifecycleStatus.Reviewing, state.Task.Status);
        Assert.Single(state.Events, item => item.Type == "WorkerFinalReportReceived");
        var events = await eventRepository.ListAsync(fixture.Project.Id, fixture.Task.TaskId, 100);
        var finalReport = Assert.Single(events, item => item.Type == "WorkerFinalReportReceived");
        var handoff = Assert.Single(events, item => item.Type == "WorkerToLeaderHandoff");
        Assert.Contains("delivered", finalReport.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("delivered", handoff.Payload, StringComparison.Ordinal);
        using var handoffJson = JsonDocument.Parse(handoff.Payload);
        Assert.Equal(finalReport.EventId.ToString(), handoffJson.RootElement.GetProperty("SourceEventId").GetString());
    }

    [Fact]
    public async Task Replaying_final_report_does_not_create_a_second_canonical_body_or_handoff()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.True(await new TaskRepository(fixture.Database).UpdateStatusAsync(fixture.Project.Id, fixture.Task.TaskId,
            TaskLifecycleStatus.Draft, TaskLifecycleStatus.ReadyToStart));
        var eventRepository = new TaskEventRepository(fixture.Database);
        var router = new WorkerSessionRouter(fixture.Registry, new TaskEventWorkerRoutingStore(eventRepository), TimeProvider.System,
            new AssignmentReviewStateRepository(fixture.Database));
        fixture.Runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
            FinalReport("first"), null), DateTimeOffset.UtcNow));
        var first = await router.StartAsync(fixture.NewRequest("prompt"));
        fixture.Runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
            FinalReport("replay"), null), DateTimeOffset.UtcNow));

        var replay = await router.StartAsync(fixture.NewRequest("replay", first.WorkerSession!.Id));

        Assert.True(replay.Succeeded);
        var events = await eventRepository.ListAsync(fixture.Project.Id, fixture.Task.TaskId, 100);
        Assert.Single(events, item => item.Type == "WorkerFinalReportReceived");
        Assert.Single(events, item => item.Type == "WorkerToLeaderHandoff");
    }

    [Fact]
    public async Task Non_final_report_handoff_keeps_its_only_persisted_message()
    {
        await using var fixture = await Fixture.CreateAsync();
        var eventRepository = new TaskEventRepository(fixture.Database);
        var router = new WorkerSessionRouter(fixture.Registry, new TaskEventWorkerRoutingStore(eventRepository), TimeProvider.System);
        fixture.Runtime.QueueTurn(new AgentTurnCompleted(new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed,
            "unstructured worker result", null), DateTimeOffset.UtcNow));

        Assert.True((await router.StartAsync(fixture.NewRequest("prompt"))).Succeeded);

        var events = await eventRepository.ListAsync(fixture.Project.Id, fixture.Task.TaskId, 100);
        var handoff = Assert.Single(events, item => item.Type == "WorkerToLeaderHandoff");
        Assert.Contains("unstructured worker result", handoff.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain(events, item => item.Type == "WorkerFinalReportReceived");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly AppTestContext _context;
        private Fixture(AppTestContext context, FakeAgentRuntime runtime, TestTaskEventStore events, StoredTask task)
        {
            _context = context;
            Runtime = runtime;
            Events = events;
            Task = task;
            Project = new Workbench.Core.Projects.Project(task.ProjectId, "Project", "C:/Project", Workbench.Core.Projects.ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            Profile = ExecutionProfile.Create(runtime.Provider.Id.Value, runtime.Account.Id.Value.ToString(), "model-a", "runtime");
            Registry = new AgentRuntimeRegistry(); Registry.Register(runtime);
            Router = new WorkerSessionRouter(Registry, events, TimeProvider.System);
        }

        public FakeAgentRuntime Runtime { get; }
        public TestTaskEventStore Events { get; }
        public StoredTask Task { get; }
        public Workbench.Core.Projects.Project Project { get; }
        public ExecutionProfile Profile { get; }
        public WorkerSessionRouter Router { get; }
        public AgentRuntimeRegistry Registry { get; }
        public Workbench.Storage.Database.WorkbenchDatabase Database => _context.Services.Database;

        public WorkerStartRequest NewRequest(string prompt, AgentSessionId? reuse = null, Func<WorkerHandoff, CancellationToken, Task>? onHandoff = null, bool waitForCompletion = true) =>
            new(Project, Task.TaskId, Task.CurrentRevisionId, Task.Title, Profile, prompt, reuse, "Worker 1", onHandoff, WaitForCompletion: waitForCompletion);

        public AgentSession CreateExistingWorker() => new(AgentSessionId.New(), Runtime.Account.Id, Runtime.Provider.Id, "model-a", Project.RootPath, "worker-existing", AgentSessionStatus.Ready, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        public static async Task<Fixture> CreateAsync()
        {
            var runtime = new FakeAgentRuntime();
            var registry = new AgentRuntimeRegistry(); registry.Register(runtime);
            var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
            var opened = await context.Services.ProjectOpenService.OpenAsync(new TemporaryDirectory().Path);
            var profile = ExecutionProfile.Create(runtime.Provider.Id.Value, runtime.Account.Id.Value.ToString(), "model-a", "runtime");
            var revision = new TaskRevision(Guid.NewGuid(), 1, "goal", "scope", "out", ["accept"], TaskRiskLevel.Low, profile, "initial", TaskRevisionApprover.User, DateTimeOffset.UtcNow, null);
            var draft = new TaskDraft(revision.TaskId, "Task title", "goal", "scope", "out", ["accept"], TaskRiskLevel.Low, profile, DateTimeOffset.UtcNow, revision);
            await context.Services.TaskRepository.CreateAsync(opened.Project.Id, draft);
            return new Fixture(context, runtime, new TestTaskEventStore(context.Services.Database), (await context.Services.TaskRepository.GetAsync(opened.Project.Id, draft.TaskId))!);
        }

        public Task<IReadOnlyList<StoredTaskEvent>> ListAsync() =>
            new TaskEventRepository(_context.Services.Database).ListAsync(Project.Id, Task.TaskId, 100);

        public Task AppendAsync(StoredTaskEvent item) =>
            new TaskEventRepository(_context.Services.Database).AppendAsync(item);

        public async ValueTask DisposeAsync() => await _context.DisposeAsync();
    }

    private static string FinalReport(string message) => JsonSerializer.Serialize(new { Kind = "FinalReport", Message = message, ValidationSummary = "verified" });
}

internal sealed class TestTaskEventStore(WorkbenchDatabase database) : IWorkerRoutingStore
{
    private readonly TaskEventRepository _events = new(database);
    private readonly List<WorkerSessionRecord> _sessions = [];
    public bool FailAppend { get; set; }
    public Task SaveSessionAsync(WorkerSessionRecord session, CancellationToken cancellationToken = default)
    {
        if (FailAppend) return Task.FromException(new InvalidOperationException("persistence failed"));
        _sessions.Add(session);
        return _events.AppendAsync(new StoredTaskEvent(Guid.NewGuid(), session.ProjectId, session.TaskId, null, "WorkerSessionStarted", JsonSerializer.Serialize(session.Label), session.LastActiveAt), cancellationToken);
    }
    public Task<IReadOnlyList<WorkerSessionRecord>> ListSessionsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkerSessionRecord>>(_sessions.Where(item => item.ProjectId == projectId).ToArray());
    public Task<WorkerSessionRecord?> GetSessionAsync(Guid projectId, Guid taskId, AgentSessionId sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_sessions.LastOrDefault(x => x.ProjectId == projectId && x.TaskId == taskId && x.Session.Id == sessionId));
    public Task AppendHandoffAsync(WorkerHandoff handoff, CancellationToken cancellationToken = default) =>
        _events.AppendAsync(new StoredTaskEvent(Guid.NewGuid(), handoff.ProjectId, handoff.TaskId, null, "WorkerToLeaderHandoff", JsonSerializer.Serialize(handoff), handoff.CreatedAt), cancellationToken);
    public Task AppendRemovalAsync(WorkerRemoval removal, CancellationToken cancellationToken = default) =>
        _events.AppendAsync(new StoredTaskEvent(Guid.NewGuid(), removal.ProjectId, removal.TaskId, null, "WorkerRemoved", JsonSerializer.Serialize(removal), removal.RemovedAt), cancellationToken);
}

internal static class WorkerRoutingEvent
{
    public static StoredTaskEvent SessionStarted(Guid projectId, Guid taskId, AgentSession session, ExecutionProfile profile, string label, DateTimeOffset at) =>
        new(Guid.NewGuid(), projectId, taskId, null, "WorkerSessionStarted", JsonSerializer.Serialize(new WorkerSessionRecord(projectId, taskId, "Task", session, profile, label, at)), at);
}
