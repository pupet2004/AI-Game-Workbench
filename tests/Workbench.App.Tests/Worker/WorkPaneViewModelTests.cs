using Workbench.App.Tests.Support;
using Workbench.App.ViewModels.Panes;
using Workbench.App.Worker;
using Workbench.Core.Tasks;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.Storage.Workers;

namespace Workbench.App.Tests.Worker;

public sealed class WorkPaneViewModelTests
{
    [Fact]
    public async Task Restores_worker_card_from_persisted_routing_event_after_reconstructing_the_store()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var projectDirectory = new TemporaryDirectory();
        var project = (await context.Services.ProjectOpenService.OpenAsync(projectDirectory.Path)).Project;
        var runtime = new FakeAgentRuntime();
        var profile = Profile(runtime);
        var lastActiveAt = DateTimeOffset.Parse("2026-08-13T10:15:00.0000000+00:00");
        var revision = new TaskRevision(Guid.NewGuid(), 1, "goal", "scope", "out", ["accept"], TaskRiskLevel.Low, profile, "initial", TaskRevisionApprover.User, lastActiveAt, null);
        var draft = new TaskDraft(revision.TaskId, "听牌茶盏", "goal", "scope", "out", ["accept"], TaskRiskLevel.Low, profile, lastActiveAt, revision);
        await context.Services.TaskRepository.CreateAsync(project.Id, draft);
        var record = new WorkerSessionRecord(
            project.Id,
            draft.TaskId,
            "听牌茶盏",
            new AgentSession(AgentSessionId.New(), runtime.Account.Id, runtime.Provider.Id, "model-a", "C:/Project", "session", AgentSessionStatus.Completed, lastActiveAt, lastActiveAt),
            profile,
            "Terra Medium",
            lastActiveAt);
        await new TaskEventWorkerRoutingStore(new TaskEventRepository(context.Services.Database)).SaveSessionAsync(record);

        var restoredStore = new TaskEventWorkerRoutingStore(new TaskEventRepository(context.Services.Database));
        var pane = new WorkPaneViewModel(() => Task.CompletedTask, restoredStore, new AgentRuntimeRegistry());

        await pane.LoadAsync(project.Id);

        var card = Assert.Single(pane.Workers);
        Assert.Equal(record.TaskTitle, card.TaskTitle);
        Assert.Equal(record.Label, card.WorkerLabel);
        Assert.Equal(profile.ModelProfileId, card.Profile);
        Assert.Equal("Completed", card.Status);
        Assert.Equal(lastActiveAt.LocalDateTime.ToString("g"), card.LastActiveAtText);
    }

    [Fact]
    public async Task Projects_the_latest_worker_handoff_status_over_the_started_session()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var projectDirectory = new TemporaryDirectory();
        var project = (await context.Services.ProjectOpenService.OpenAsync(projectDirectory.Path)).Project;
        var runtime = new FakeAgentRuntime();
        var profile = Profile(runtime);
        var startedAt = DateTimeOffset.Parse("2026-08-13T10:15:00.0000000+00:00");
        var completedAt = startedAt.AddMinutes(2);
        var taskId = Guid.NewGuid();
        var revision = new TaskRevision(taskId, 1, "goal", "scope", "out", ["accept"], TaskRiskLevel.Low, profile, "initial", TaskRevisionApprover.User, startedAt, null);
        await context.Services.TaskRepository.CreateAsync(project.Id, new TaskDraft(taskId, "Read smoke context", "goal", "scope", "out", ["accept"], TaskRiskLevel.Low, profile, startedAt, revision));
        var session = new AgentSession(AgentSessionId.New(), runtime.Account.Id, runtime.Provider.Id, "model-a", "C:/Project", "session", AgentSessionStatus.Ready, startedAt, startedAt);
        var store = new TaskEventWorkerRoutingStore(new TaskEventRepository(context.Services.Database));
        await store.SaveSessionAsync(new WorkerSessionRecord(project.Id, taskId, "Read smoke context", session, profile, "Worker", startedAt));
        await store.AppendHandoffAsync(new WorkerHandoff(project.Id, taskId, session.Id, "Worker", AgentSessionStatus.Running, "Intermediate", startedAt.AddMinutes(1)));
        await store.AppendHandoffAsync(new WorkerHandoff(project.Id, taskId, session.Id, "Worker", AgentSessionStatus.Completed, "Final report", completedAt));
        var pane = new WorkPaneViewModel(() => Task.CompletedTask, new TaskEventWorkerRoutingStore(new TaskEventRepository(context.Services.Database)), new AgentRuntimeRegistry());

        await pane.LoadAsync(project.Id);

        var card = Assert.Single(pane.Workers);
        Assert.Equal("Completed", card.Status);
        Assert.Equal(completedAt.LocalDateTime.ToString("g"), card.LastActiveAtText);
    }

    [Fact]
    public async Task Loads_persisted_worker_cards_with_task_profile_status_and_completed_sessions_retained()
    {
        var store = new InMemoryWorkerRoutingStore();
        var projectId = Guid.NewGuid();
        await store.SaveSessionAsync(Session(projectId, Guid.NewGuid(), "听牌茶盏", "Terra Medium", AgentSessionStatus.Running, TimeSpan.FromMinutes(-1)));
        await store.SaveSessionAsync(Session(projectId, Guid.NewGuid(), "遗物目录", "Luna Low", AgentSessionStatus.Completed, TimeSpan.FromMinutes(-2)));
        var pane = new WorkPaneViewModel(() => Task.CompletedTask, store, new AgentRuntimeRegistry());

        await pane.LoadAsync(projectId);

        Assert.Equal(2, pane.Workers.Count);
        Assert.Equal("听牌茶盏", pane.Workers[0].TaskTitle);
        Assert.Equal("Terra Medium", pane.Workers[0].WorkerLabel);
        Assert.Equal("Working", pane.Workers[0].Status);
        Assert.Equal("Completed", pane.Workers[1].Status);
        Assert.NotEmpty(pane.Workers[1].LastActiveAtText);
    }

    [Fact]
    public async Task Selecting_a_worker_loads_only_that_workers_transcript_without_creating_a_session()
    {
        var runtime = new FakeAgentRuntime();
        var session = new AgentSession(AgentSessionId.New(), runtime.Account.Id, runtime.Provider.Id, "model-a", "C:/Project", "session", AgentSessionStatus.Ready, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var record = new WorkerSessionRecord(Guid.NewGuid(), Guid.NewGuid(), "Task A", session, Profile(runtime), "Worker A", DateTimeOffset.UtcNow);
        runtime.Transcript = [new AgentMessage(AgentMessageRole.Assistant, "Worker says hello", DateTimeOffset.UtcNow)];
        var registry = new AgentRuntimeRegistry(); registry.Register(runtime);
        var store = new InMemoryWorkerRoutingStore(); await store.SaveSessionAsync(record with { Session = session });
        var pane = new WorkPaneViewModel(() => Task.CompletedTask, store, registry);
        await pane.LoadAsync(store.ProjectId);

        await pane.SelectWorkerAsync(pane.Workers.Single());

        Assert.Equal("Worker says hello", pane.Transcript.Single().Text);
        Assert.Empty(runtime.CreateRequests);
        Assert.Single(runtime.TranscriptRequests);
    }

    [Fact]
    public async Task Opening_a_codex_worker_reuses_its_persisted_session_without_changing_its_status()
    {
        var runtime = new FakeAgentRuntime();
        var session = new AgentSession(
            AgentSessionId.New(), runtime.Account.Id, new Workbench.Runtime.Providers.ProviderId("codex"),
            "gpt-5.6-sol", "C:/Project", "thread-existing", AgentSessionStatus.Completed,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var record = new WorkerSessionRecord(Guid.NewGuid(), Guid.NewGuid(), "Read smoke context", session,
            ExecutionProfile.Create("codex", runtime.Account.Id.Value.ToString(), "gpt-5.6-sol", "codex-app-server"),
            "Worker", DateTimeOffset.UtcNow);
        var store = new InMemoryWorkerRoutingStore();
        await store.SaveSessionAsync(record);
        var launcher = new RecordingInteractiveSessionLauncher();
        var pane = new WorkPaneViewModel(() => Task.CompletedTask, store, new AgentRuntimeRegistry(), launcher);
        await pane.LoadAsync(record.ProjectId);

        await pane.OpenWorkerAsync(Assert.Single(pane.Workers));
        await pane.OpenWorkerAsync(Assert.Single(pane.Workers));

        Assert.Equal([record.Session.Id, record.Session.Id], launcher.Opened.Select(item => item.Session.Id));
        Assert.All(launcher.Opened, item => Assert.Equal("thread-existing", item.Session.ExternalSessionId));
        Assert.All(launcher.Opened, item => Assert.Equal("C:/Project", item.Session.WorkingDirectory));
        Assert.Equal("Completed", pane.Workers.Single().Status);
        Assert.Single(await store.ListSessionsAsync(record.ProjectId));
    }

    [Fact]
    public async Task Codex_launcher_rejects_non_codex_workers_without_starting_a_window()
    {
        var runtime = new FakeAgentRuntime();
        var record = Session(Guid.NewGuid(), Guid.NewGuid(), "Task", "Worker", AgentSessionStatus.Ready, TimeSpan.Zero);
        var starts = 0;
        var launcher = new CodexInteractiveSessionLauncher(_ =>
        {
            starts++;
        });

        var result = await launcher.OpenAsync(record);

        Assert.False(result.Succeeded);
        Assert.Equal("This Worker runtime does not support native Codex sessions.", result.Error);
        Assert.Equal(0, starts);
    }

    [Fact]
    public void Codex_launcher_starts_a_new_native_terminal_that_resumes_the_existing_thread_in_the_project_directory()
    {
        var runtime = new FakeAgentRuntime();
        var session = new AgentSession(
            AgentSessionId.New(), runtime.Account.Id, new Workbench.Runtime.Providers.ProviderId("codex"),
            "gpt-5.6-sol", "C:/Project", "thread-existing", AgentSessionStatus.Completed,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var record = new WorkerSessionRecord(Guid.NewGuid(), Guid.NewGuid(), "Read smoke context", session,
            ExecutionProfile.Create("codex", runtime.Account.Id.Value.ToString(), "gpt-5.6-sol", "codex-app-server"),
            "Worker", DateTimeOffset.UtcNow);

        var startInfo = CodexInteractiveSessionLauncher.CreateStartInfo(record);

        Assert.Equal("wt.exe", startInfo.FileName);
        Assert.Equal(
            ["-w", "new", "--size", "120,42", "--title", "[Worker] Read smoke context - Codex",
             "--suppressApplicationTitle", "-d", "C:/Project", "cmd.exe", "/k", "codex", "resume",
             "thread-existing", "-C", "C:/Project"],
            startInfo.ArgumentList);
    }

    private static WorkerSessionRecord Session(Guid projectId, Guid taskId, string title, string label, AgentSessionStatus status, TimeSpan age)
    {
        var runtime = new FakeAgentRuntime();
        return new WorkerSessionRecord(projectId, taskId, title,
            new AgentSession(AgentSessionId.New(), runtime.Account.Id, runtime.Provider.Id, "model-a", "C:/Project", "session", status, DateTimeOffset.UtcNow.Add(age), DateTimeOffset.UtcNow.Add(age)),
            Profile(runtime), label, DateTimeOffset.UtcNow.Add(age));
    }

    private static ExecutionProfile Profile(FakeAgentRuntime runtime) => ExecutionProfile.Create(runtime.Provider.Id.Value, runtime.Account.Id.Value.ToString(), "model-a", "runtime");
}

internal sealed class RecordingInteractiveSessionLauncher : IAgentInteractiveSessionLauncher
{
    public List<WorkerSessionRecord> Opened { get; } = [];

    public bool CanOpen(WorkerSessionRecord worker) => true;

    public Task<InteractiveSessionOpenResult> OpenAsync(WorkerSessionRecord worker, CancellationToken cancellationToken = default)
    {
        Opened.Add(worker);
        return Task.FromResult(InteractiveSessionOpenResult.Success());
    }
}

internal sealed class InMemoryWorkerRoutingStore : IWorkerRoutingStore
{
    private readonly List<WorkerSessionRecord> _sessions = [];
    public Guid ProjectId => _sessions.First().ProjectId;
    public Task SaveSessionAsync(WorkerSessionRecord session, CancellationToken cancellationToken = default) { _sessions.Add(session); return Task.CompletedTask; }
    public Task<IReadOnlyList<WorkerSessionRecord>> ListSessionsAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkerSessionRecord>>(_sessions.Where(item => item.ProjectId == projectId).ToArray());
    public Task<WorkerSessionRecord?> GetSessionAsync(Guid projectId, Guid taskId, AgentSessionId sessionId, CancellationToken cancellationToken = default) => Task.FromResult(_sessions.LastOrDefault(item => item.ProjectId == projectId && item.TaskId == taskId && item.Session.Id == sessionId));
    public Task AppendHandoffAsync(WorkerHandoff handoff, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
