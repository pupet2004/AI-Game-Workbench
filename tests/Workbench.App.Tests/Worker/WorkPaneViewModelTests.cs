using Workbench.App.Tests.Support;
using Workbench.App.ViewModels.Panes;
using Workbench.App.Worker;
using Workbench.Core.Tasks;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.Storage.Workers;
using Workbench.App.AgentHost;

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
    public async Task Opening_a_running_codex_worker_is_blocked_to_avoid_single_writer_conflict()
    {
        var runtime = new FakeAgentRuntime();
        var session = new AgentSession(
            AgentSessionId.New(), runtime.Account.Id, new Workbench.Runtime.Providers.ProviderId("codex"),
            "gpt-5.6-sol", "C:/Project", "thread-running", AgentSessionStatus.Running,
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(-1));
        var record = new WorkerSessionRecord(Guid.NewGuid(), Guid.NewGuid(), "Running task", session,
            ExecutionProfile.Create("codex", runtime.Account.Id.Value.ToString(), "gpt-5.6-sol", "codex-app-server"),
            "Worker", DateTimeOffset.UtcNow.AddMinutes(-1));
        var store = new InMemoryWorkerRoutingStore();
        await store.SaveSessionAsync(record);
        var launcher = new RecordingInteractiveSessionLauncher();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        var pane = new WorkPaneViewModel(() => Task.CompletedTask, store, registry, launcher);
        await pane.LoadAsync(record.ProjectId);

        await pane.OpenWorkerAsync(Assert.Single(pane.Workers));

        Assert.Empty(launcher.Opened);
        Assert.Contains("running in Workbench", pane.OpenError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reconstructing_with_a_live_cli_lease_does_not_claim_ownership_when_runtime_release_fails()
    {
        var runtime = new FakeAgentRuntime();
        var record = Session(Guid.NewGuid(), Guid.NewGuid(), "Attached task", "Worker", AgentSessionStatus.Completed, TimeSpan.Zero)
            with
            {
                Session = new AgentSession(
                    AgentSessionId.New(), runtime.Account.Id, new Workbench.Runtime.Providers.ProviderId("codex"),
                    "gpt-5.6-sol", "C:/Project", "thread-existing", AgentSessionStatus.Completed,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                Profile = ExecutionProfile.Create("codex", runtime.Account.Id.Value.ToString(), "gpt-5.6-sol", "codex-app-server")
            };
        var store = new InMemoryWorkerRoutingStore();
        await store.SaveSessionAsync(record);
        await store.AppendSurfaceLeaseAsync(new WorkerSessionSurfaceLease(
            record.ProjectId, record.TaskId, record.Session.Id, Guid.NewGuid(),
            Environment.ProcessId, true, DateTimeOffset.UtcNow, WorkerSessionSurfaceState.CliOwned));

        var pane = new WorkPaneViewModel(
            () => Task.CompletedTask,
            store,
            new AgentRuntimeRegistry(),
            releaseRuntimeForExternalCli: (_, _) => Task.FromResult(false));

        await pane.LoadAsync(record.ProjectId);

        var card = Assert.Single(pane.Workers);
        Assert.True(card.IsExternalCliActive);
        Assert.Equal("需要恢复监管", card.SurfaceStatus);
        Assert.Contains("无法安全释放", pane.OpenError, StringComparison.Ordinal);
        var lease = await store.GetActiveSurfaceLeaseAsync(record.ProjectId, record.TaskId, record.Session.Id);
        Assert.Equal(WorkerSessionSurfaceState.RecoveryRequired, lease?.State);
    }

    [Fact]
    public async Task Card_activation_selects_the_worker_without_opening_a_second_process()
    {
        var runtime = new FakeAgentRuntime();
        var session = new AgentSession(
            AgentSessionId.New(), runtime.Account.Id, new Workbench.Runtime.Providers.ProviderId("codex"),
            "gpt-5.6-sol", "C:/Project", "thread-card", AgentSessionStatus.Interrupted,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var record = new WorkerSessionRecord(Guid.NewGuid(), Guid.NewGuid(), "Card task", session,
            ExecutionProfile.Create("codex", runtime.Account.Id.Value.ToString(), "gpt-5.6-sol", "codex-app-server"),
            "Worker", DateTimeOffset.UtcNow);
        var store = new InMemoryWorkerRoutingStore();
        await store.SaveSessionAsync(record);
        var launcher = new RecordingInteractiveSessionLauncher();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        var pane = new WorkPaneViewModel(() => Task.CompletedTask, store, registry, launcher);
        await pane.LoadAsync(record.ProjectId);

        await pane.SelectWorkerAsync(Assert.Single(pane.Workers));

        Assert.Empty(launcher.Opened);
        Assert.Equal(record.Session.Id, pane.SelectedWorker!.Session.Id);
        Assert.Equal("Interrupted", pane.Workers.Single().Status);
        Assert.Empty(runtime.CreatedSessions);
    }

    [Fact]
    public async Task Confirmed_removal_hides_the_worker_after_reconstructing_the_store_without_deleting_its_history()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var projectDirectory = new TemporaryDirectory();
        var project = (await context.Services.ProjectOpenService.OpenAsync(projectDirectory.Path)).Project;
        var runtime = new FakeAgentRuntime();
        var profile = Profile(runtime);
        var startedAt = DateTimeOffset.Parse("2026-08-14T10:15:00.0000000+00:00");
        var taskId = Guid.NewGuid();
        var revision = new TaskRevision(taskId, 1, "goal", "scope", "out", ["accept"], TaskRiskLevel.Low, profile, "initial", TaskRevisionApprover.User, startedAt, null);
        await context.Services.TaskRepository.CreateAsync(project.Id, new TaskDraft(taskId, "Preserve history", "goal", "scope", "out", ["accept"], TaskRiskLevel.Low, profile, startedAt, revision));
        var session = new AgentSession(AgentSessionId.New(), runtime.Account.Id, runtime.Provider.Id, "model-a", project.RootPath, "thread-preserved", AgentSessionStatus.Completed, startedAt, startedAt);
        var record = new WorkerSessionRecord(project.Id, taskId, "Preserve history", session, profile, "Worker", startedAt);
        var events = new TaskEventRepository(context.Services.Database);
        var store = new TaskEventWorkerRoutingStore(events);
        await store.SaveSessionAsync(record);
        await store.AppendHandoffAsync(new WorkerHandoff(project.Id, taskId, session.Id, "Worker", AgentSessionStatus.Completed, "Final report remains", startedAt.AddMinutes(1)));

        var result = await new WorkerRemovalService(new AgentRuntimeRegistry(), store, TimeProvider.System).RemoveAsync(record);

        Assert.True(result.Succeeded);
        Assert.Empty(await new TaskEventWorkerRoutingStore(new TaskEventRepository(context.Services.Database)).ListSessionsAsync(project.Id));
        var history = await events.ListAsync(project.Id, taskId, 20);
        Assert.Contains(history, item => item.Type == "WorkerSessionStarted" && item.Payload.Contains("thread-preserved", StringComparison.Ordinal));
        Assert.Contains(history, item => item.Type == "WorkerToLeaderHandoff" && item.Payload.Contains("Final report remains", StringComparison.Ordinal));
        Assert.Contains(history, item => item.Type == "WorkerRemoved");
    }

    [Fact]
    public async Task Removing_a_working_worker_stops_active_execution_before_appending_the_removal_fact()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        var store = new InMemoryWorkerRoutingStore();
        var record = Session(Guid.NewGuid(), Guid.NewGuid(), "Working task", "Worker", AgentSessionStatus.Running, TimeSpan.Zero) with
        {
            Session = new AgentSession(AgentSessionId.New(), runtime.Account.Id, runtime.Provider.Id, "model-a", "C:/Project", "thread-working", AgentSessionStatus.Running, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        };
        await store.SaveSessionAsync(record);

        var result = await new WorkerRemovalService(registry, store, TimeProvider.System).RemoveAsync(record);

        Assert.True(result.Succeeded);
        Assert.Equal(record.Session.Id, Assert.Single(runtime.StoppedSessions).Id);
        Assert.Single(store.Removals);
        Assert.Empty(await store.ListSessionsAsync(record.ProjectId));
    }

    [Fact]
    public async Task Failed_stop_keeps_a_working_worker_visible_and_does_not_append_removal()
    {
        var runtime = new FakeAgentRuntime { StopException = new InvalidOperationException("stop failed") };
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        var store = new InMemoryWorkerRoutingStore();
        var record = new WorkerSessionRecord(Guid.NewGuid(), Guid.NewGuid(), "Working task",
            new AgentSession(AgentSessionId.New(), runtime.Account.Id, runtime.Provider.Id, "model-a", "C:/Project", "thread-working", AgentSessionStatus.Running, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            Profile(runtime), "Worker", DateTimeOffset.UtcNow);
        await store.SaveSessionAsync(record);

        var result = await new WorkerRemovalService(registry, store, TimeProvider.System).RemoveAsync(record);

        Assert.False(result.Succeeded);
        Assert.Equal("stop failed", result.Error);
        Assert.Empty(store.Removals);
        Assert.Single(await store.ListSessionsAsync(record.ProjectId));
    }

    [Fact]
    public async Task Cancelling_removal_keeps_the_worker_visible_without_appending_a_removal_fact()
    {
        var runtime = new FakeAgentRuntime();
        var store = new InMemoryWorkerRoutingStore();
        var record = Session(Guid.NewGuid(), Guid.NewGuid(), "Keep task", "Worker", AgentSessionStatus.Completed, TimeSpan.Zero);
        await store.SaveSessionAsync(record);
        var pane = new WorkPaneViewModel(() => Task.CompletedTask, store, new AgentRuntimeRegistry());
        await pane.LoadAsync(record.ProjectId);

        pane.RequestWorkerRemovalCommand.Execute(Assert.Single(pane.Workers));
        pane.CancelWorkerRemovalCommand.Execute(null);

        Assert.Null(pane.PendingWorkerRemoval);
        Assert.Single(pane.Workers);
        Assert.Empty(store.Removals);
    }

    [Fact]
    public void Worker_card_markup_activates_on_left_click_without_intercepting_the_open_button()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var markup = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Workbench.App", "Views", "Panes", "WorkPaneView.axaml"));
        var codeBehind = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Workbench.App", "Views", "Panes", "WorkPaneView.axaml.cs"));

        Assert.Contains("PointerPressed=\"OnWorkerCardPointerPressed\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("OnOpenButtonPointerPressed", markup, StringComparison.Ordinal);
        Assert.Contains("FindAncestorOfType<Button>()", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_card_markup_offers_a_right_click_removal_confirmation_without_card_activation()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var markup = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Workbench.App", "Views", "Panes", "WorkPaneView.axaml"));
        var codeBehind = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Workbench.App", "Views", "Panes", "WorkPaneView.axaml.cs"));

        Assert.Contains("ConfirmWorkerRemovalCommand", markup, StringComparison.Ordinal);
        Assert.Contains("CancelWorkerRemovalCommand", markup, StringComparison.Ordinal);
        Assert.Contains("OnWorkerCardPointerPressed", markup, StringComparison.Ordinal);
        Assert.Contains("OnWorkerActionsClick", markup, StringComparison.Ordinal);
        Assert.Contains("<ItemsControl IsVisible=\"{Binding HasWorkers}\"", markup, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"8\"", markup, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", markup, StringComparison.Ordinal);
        Assert.Contains("标记为已结束", codeBehind, StringComparison.Ordinal);
        Assert.Contains("从 Workbench 移除", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_scrollbar_stays_thin_when_pointer_is_over_it()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var markup = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Workbench.App", "Views", "Panes", "WorkPaneView.axaml"));

        Assert.Contains("<Style Selector=\"ScrollBar\">", markup, StringComparison.Ordinal);
        Assert.Contains("<Style Selector=\"ScrollBar:pointerover\">", markup, StringComparison.Ordinal);
        Assert.True(markup.Split("<Setter Property=\"Width\" Value=\"4\" />", StringSplitOptions.None).Length - 1 >= 2);
        Assert.True(markup.Split("<Setter Property=\"MaxWidth\" Value=\"4\" />", StringSplitOptions.None).Length - 1 >= 2);
    }

    [Fact]
    public void Waiting_worker_is_not_presented_as_working()
    {
        var runtime = new FakeAgentRuntime();
        var session = new AgentSession(AgentSessionId.New(), runtime.Account.Id, runtime.Provider.Id, "model-a", "C:/Project", "thread-waiting", AgentSessionStatus.WaitingApproval, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var card = new WorkerSessionCardViewModel(new WorkerSessionRecord(Guid.NewGuid(), Guid.NewGuid(), "Waiting task", session, Profile(runtime), "Worker", DateTimeOffset.UtcNow));

        Assert.NotEqual("Working", card.Status);
    }

    [Fact]
    public void Worker_progress_is_incremental_and_terminal_status_does_not_complete_unreported_steps()
    {
        var runtime = new FakeAgentRuntime();
        var session = new AgentSession(AgentSessionId.New(), runtime.Account.Id, runtime.Provider.Id, "model-a", "C:/Project", "thread-progress", AgentSessionStatus.Completed, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var record = new WorkerSessionRecord(Guid.NewGuid(), Guid.NewGuid(), "Progress task", session, Profile(runtime), "Worker", DateTimeOffset.UtcNow);
        var card = new WorkerSessionCardViewModel(record, ["one", "two", "three"]);

        Assert.Equal("0/3", card.ProgressText);
        card.ApplyProgress(1);
        Assert.Equal("1/3", card.ProgressText);
        Assert.Equal("✓", card.PlanSteps[0].Marker);
        Assert.Equal("●", card.PlanSteps[1].Marker);
    }

    [Fact]
    public void Explicit_completion_markers_advance_a_native_provider_plan()
    {
        var runtime = new FakeAgentRuntime();
        var session = new AgentSession(AgentSessionId.New(), runtime.Account.Id, runtime.Provider.Id, "model-a", "C:/Project", "thread-progress", AgentSessionStatus.Running, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var record = new WorkerSessionRecord(Guid.NewGuid(), Guid.NewGuid(), "Progress task", session, Profile(runtime), "Worker", DateTimeOffset.UtcNow);
        var card = new WorkerSessionCardViewModel(record, ["one", "two", "three"]);

        card.ApplyPlanUpdate([
            new AgentPlanStep("step-1", "one", AgentPlanStepStatus.InProgress),
            new AgentPlanStep("step-2", "two", AgentPlanStepStatus.Pending),
            new AgentPlanStep("step-3", "three", AgentPlanStepStatus.Pending)]);
        card.ApplyProgress(1);

        Assert.Equal("1/3", card.ProgressText);
        Assert.Equal("✓", card.PlanSteps[0].Marker);
        Assert.Equal("●", card.PlanSteps[1].Marker);
        Assert.Equal("○", card.PlanSteps[2].Marker);
    }

    [Fact]
    public void Worker_progress_snapshot_restores_completed_steps()
    {
        var runtime = new FakeAgentRuntime();
        var session = new AgentSession(AgentSessionId.New(), runtime.Account.Id, runtime.Provider.Id, "model-a", "C:/Project", "thread-progress", AgentSessionStatus.Running, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var record = new WorkerSessionRecord(Guid.NewGuid(), Guid.NewGuid(), "Progress task", session, Profile(runtime), "Worker", DateTimeOffset.UtcNow);
        var card = new WorkerSessionCardViewModel(record, ["one", "two"]);
        card.ApplyProgress(1);
        var restored = new WorkerSessionCardViewModel(record, ["one", "two"]);

        restored.ApplyProgressSnapshot(card.CreateProgressSnapshot().Steps);

        Assert.Equal("1/2", restored.ProgressText);
        Assert.Equal("✓", restored.PlanSteps[0].Marker);
        Assert.Equal("●", restored.PlanSteps[1].Marker);
    }

    [Fact]
    public async Task User_can_mark_a_worker_completed_without_deleting_its_session_record()
    {
        var runtime = new FakeAgentRuntime();
        var store = new InMemoryWorkerRoutingStore();
        var record = Session(Guid.NewGuid(), Guid.NewGuid(), "Stale task", "Worker", AgentSessionStatus.Running, TimeSpan.Zero);
        await store.SaveSessionAsync(record);
        var pane = new WorkPaneViewModel(() => Task.CompletedTask, store, new AgentRuntimeRegistry());
        await pane.LoadAsync(record.ProjectId);

        await pane.MarkWorkerCompletedCommand.ExecuteAsync(Assert.Single(pane.Workers));

        var card = Assert.Single(pane.Workers);
        Assert.Equal("Completed", card.Status);
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
    public async Task Codex_launcher_does_not_start_cli_when_workbench_cannot_release_runtime()
    {
        var runtime = new FakeAgentRuntime();
        var session = new AgentSession(
            AgentSessionId.New(), runtime.Account.Id, new Workbench.Runtime.Providers.ProviderId("codex"),
            "gpt-5.6-sol", "C:/Project", "thread-existing", AgentSessionStatus.Completed,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var record = new WorkerSessionRecord(Guid.NewGuid(), Guid.NewGuid(), "Task", session,
            ExecutionProfile.Create("codex", runtime.Account.Id.Value.ToString(), "gpt-5.6-sol", "codex-app-server"),
            "Worker", DateTimeOffset.UtcNow);
        var starts = 0;
        var launcher = new CodexInteractiveSessionLauncher(
            _ =>
            {
                starts++;
                return null;
            },
            (_, _) => Task.FromResult(false));

        var result = await launcher.OpenAsync(record);

        Assert.False(result.Succeeded);
        Assert.Contains("release", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, starts);
    }

    [Fact]
    public async Task Codex_launcher_does_not_release_or_start_cli_for_an_active_worker()
    {
        var runtime = new FakeAgentRuntime();
        var session = new AgentSession(
            AgentSessionId.New(), runtime.Account.Id, new Workbench.Runtime.Providers.ProviderId("codex"),
            "gpt-5.6-sol", "C:/Project", "thread-running", AgentSessionStatus.Running,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var record = new WorkerSessionRecord(Guid.NewGuid(), Guid.NewGuid(), "Running task", session,
            ExecutionProfile.Create("codex", runtime.Account.Id.Value.ToString(), "gpt-5.6-sol", "codex-app-server"),
            "Worker", DateTimeOffset.UtcNow);
        var starts = 0;
        var releases = 0;
        var launcher = new CodexInteractiveSessionLauncher(
            _ =>
            {
                starts++;
                return null;
            },
            (_, _) =>
            {
                releases++;
                return Task.FromResult(true);
            });

        var result = await launcher.OpenAsync(record);

        Assert.False(result.Succeeded);
        Assert.Contains("active in Workbench", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, starts);
        Assert.Equal(0, releases);
    }

    [Fact]
    public void Codex_launcher_starts_a_native_terminal_that_resumes_the_existing_thread_in_the_project_directory()
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
            ["--wait", "-w", "new", "--size", "120,42", "--pos", "24,24", "--title", "[Worker] Read smoke context - Codex",
             "--suppressApplicationTitle", "-d", "C:/Project", "cmd.exe", "/c", "codex", "resume",
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
    private readonly List<WorkerSessionSurfaceLease> _leases = [];
    public List<WorkerRemoval> Removals { get; } = [];
    public Guid ProjectId => _sessions.First().ProjectId;
    public Task SaveSessionAsync(WorkerSessionRecord session, CancellationToken cancellationToken = default) { _sessions.Add(session); return Task.CompletedTask; }
    public Task<IReadOnlyList<WorkerSessionRecord>> ListSessionsAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkerSessionRecord>>(_sessions.Where(item => item.ProjectId == projectId && !Removals.Any(removed => removed.WorkerSessionId == item.Session.Id)).ToArray());
    public Task<WorkerSessionRecord?> GetSessionAsync(Guid projectId, Guid taskId, AgentSessionId sessionId, CancellationToken cancellationToken = default) => Task.FromResult(_sessions.LastOrDefault(item => item.ProjectId == projectId && item.TaskId == taskId && item.Session.Id == sessionId));
    public Task AppendHandoffAsync(WorkerHandoff handoff, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task AppendSurfaceLeaseAsync(WorkerSessionSurfaceLease lease, CancellationToken cancellationToken = default) { _leases.Add(lease); return Task.CompletedTask; }
    public Task<WorkerSessionSurfaceLease?> GetActiveSurfaceLeaseAsync(Guid projectId, Guid taskId, AgentSessionId sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_leases.Where(item => item.ProjectId == projectId && item.TaskId == taskId && item.WorkerSessionId == sessionId).LastOrDefault(item => item.Active));
    public Task AppendRemovalAsync(WorkerRemoval removal, CancellationToken cancellationToken = default) { Removals.Add(removal); return Task.CompletedTask; }
    public Task OverrideStatusAsync(WorkerStatusOverride status, CancellationToken cancellationToken = default)
    {
        var index = _sessions.FindLastIndex(item => item.ProjectId == status.ProjectId && item.TaskId == status.TaskId && item.Session.Id == status.WorkerSessionId);
        if (index >= 0)
        {
            var current = _sessions[index];
            var changedAt = status.ChangedAt;
            _sessions[index] = current with
            {
                Session = current.Session with { Status = status.Status, UpdatedAt = changedAt },
                LastActiveAt = changedAt
            };
        }
        return Task.CompletedTask;
    }
}
