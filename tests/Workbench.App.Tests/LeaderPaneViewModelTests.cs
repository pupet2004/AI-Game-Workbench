using Workbench.App.Tests.Support;
using Workbench.App.Leader;
using Workbench.App.ViewModels.Leader;
using Workbench.App.ViewModels.Panes;
using Workbench.Core.Projects;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Registry;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class LeaderPaneViewModelTests
{
    [Fact]
    public async Task Leader_loads_available_models_from_registry()
    {
        var (pane, runtime, _) = CreatePane();

        await pane.InitializeAsync();

        Assert.Equal(runtime.Models.Select(model => model.ModelId), pane.AvailableModels.Select(model => model.Profile.Model.ModelId));
    }

    [Fact]
    public async Task Leader_distinguishes_account_scoped_models()
    {
        var first = new FakeAgentRuntime("Provider", "Account One");
        var second = new FakeAgentRuntime("Provider", "Account Two");
        var registry = CreateRegistry(first, second);
        var pane = CreatePane(CreateProject(), registry, new ProjectLeaderSessionManager());

        await pane.InitializeAsync();

        Assert.Equal(2, pane.AvailableModels.Count);
        Assert.Contains(pane.AvailableModels, option => option.DisplayName.Contains("Account One", StringComparison.Ordinal));
        Assert.Contains(pane.AvailableModels, option => option.DisplayName.Contains("Account Two", StringComparison.Ordinal));
        Assert.Equal(2, pane.AvailableModels.Select(option => option.Profile.AccountId).Distinct().Count());
    }

    [Fact]
    public async Task Multiple_models_require_explicit_selection()
    {
        var runtime = new FakeAgentRuntime(
            models:
            [
                new ModelProfile(new ProviderId("fake-provider"), "model-a", "Model A", AgentCapability.StructuredEvents),
                new ModelProfile(new ProviderId("fake-provider"), "model-b", "Model B", AgentCapability.StructuredEvents)
            ]);
        var pane = CreatePane(CreateProject(), CreateRegistry(runtime), new ProjectLeaderSessionManager());

        await pane.InitializeAsync();

        Assert.Null(pane.SelectedModel);
        Assert.False(pane.CanSend);
    }

    [Fact]
    public async Task Single_available_model_can_be_selected_automatically()
    {
        var (pane, _, _) = CreatePane();

        await pane.InitializeAsync();

        Assert.NotNull(pane.SelectedModel);
    }

    [Fact]
    public async Task Runtime_unavailable_does_not_break_workspace()
    {
        var pane = CreatePane(CreateProject(), new AgentRuntimeRegistry(), new ProjectLeaderSessionManager());

        await pane.InitializeAsync();

        Assert.False(pane.IsRuntimeAvailable);
        Assert.Equal("Leader unavailable", pane.RuntimeStatus);
        Assert.True(pane.CanRetryRuntime);
    }

    [Fact]
    public async Task Fresh_pane_connects_runtime_before_loading_models()
    {
        var runtime = new FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        var pane = new LeaderPaneViewModel(
            CreateProject(),
            registry,
            new ProjectLeaderSessionManager(),
            () => Task.CompletedTask,
            reconnectRuntime: _ =>
            {
                registry.Register(runtime);
                return Task.CompletedTask;
            });
        await pane.InitializeAsync();

        Assert.True(pane.IsRuntimeAvailable);
        Assert.Equal(runtime.Models.Select(model => model.ModelId), pane.AvailableModels.Select(model => model.Profile.Model.ModelId));
    }

    [Fact]
    public async Task First_message_creates_leader_session()
    {
        var (pane, runtime, _) = CreatePane();
        QueueCompletedTurn(runtime, "hello");
        await pane.InitializeAsync();

        pane.DraftMessage = "first";
        await pane.SendAsync();

        Assert.Single(runtime.CreateRequests);
        Assert.NotNull(pane.Session);
    }

    [Fact]
    public async Task Leader_session_uses_project_root_as_working_directory()
    {
        var project = CreateProject("C:/Games/ProjectA");
        var runtime = new FakeAgentRuntime();
        var pane = CreatePane(project, CreateRegistry(runtime), new ProjectLeaderSessionManager());
        QueueCompletedTurn(runtime, "done");
        await pane.InitializeAsync();

        pane.DraftMessage = "first";
        await pane.SendAsync();

        Assert.Equal(project.RootPath, Assert.Single(runtime.CreateRequests).WorkingDirectory);
    }

    [Fact]
    public async Task Second_message_reuses_same_agent_session()
    {
        var (pane, runtime, _) = CreatePane();
        QueueCompletedTurn(runtime, "one");
        QueueCompletedTurn(runtime, "two");
        await pane.InitializeAsync();

        pane.DraftMessage = "first";
        await pane.SendAsync();
        pane.DraftMessage = "second";
        await pane.SendAsync();

        Assert.Single(runtime.CreatedSessions);
        Assert.Equal(2, runtime.SentSessions.Count);
        Assert.Equal(runtime.SentSessions[0].Id, runtime.SentSessions[1].Id);
        Assert.Equal(runtime.SentSessions[0].ExternalSessionId, runtime.SentSessions[1].ExternalSessionId);
    }

    [Fact]
    public async Task Returning_to_same_project_reuses_leader_session()
    {
        var project = CreateProject();
        var runtime = new FakeAgentRuntime();
        var registry = CreateRegistry(runtime);
        var manager = new ProjectLeaderSessionManager();
        var firstPane = CreatePane(project, registry, manager);
        QueueCompletedTurn(runtime, "one");
        await firstPane.InitializeAsync();
        firstPane.DraftMessage = "first";
        await firstPane.SendAsync();

        var reopenedPane = CreatePane(project, registry, manager);
        await reopenedPane.InitializeAsync();

        Assert.Same(firstPane.Session, reopenedPane.Session);
        Assert.Equal(firstPane.Messages.Select(message => message.Text), reopenedPane.Messages.Select(message => message.Text));
    }

    [Fact]
    public async Task Different_projects_use_different_leader_sessions()
    {
        var runtime = new FakeAgentRuntime();
        var registry = CreateRegistry(runtime);
        var manager = new ProjectLeaderSessionManager();
        var first = CreatePane(CreateProject("C:/Games/A"), registry, manager);
        var second = CreatePane(CreateProject("C:/Games/B"), registry, manager);
        QueueCompletedTurn(runtime, "a");
        QueueCompletedTurn(runtime, "b");
        await first.InitializeAsync();
        await second.InitializeAsync();

        first.DraftMessage = "first";
        await first.SendAsync();
        second.DraftMessage = "second";
        await second.SendAsync();

        Assert.Equal(2, runtime.CreatedSessions.Count);
        Assert.NotEqual(first.Session!.Id, second.Session!.Id);
    }

    [Fact]
    public async Task Model_selection_is_locked_after_session_creation()
    {
        var (pane, runtime, _) = CreatePane();
        QueueCompletedTurn(runtime, "done");
        await pane.InitializeAsync();
        pane.DraftMessage = "first";

        await pane.SendAsync();

        Assert.True(pane.IsModelSelectionLocked);
        Assert.False(pane.IsModelSelectionEnabled);
    }

    [Fact]
    public async Task User_message_appears_immediately()
    {
        var (pane, runtime, _) = CreatePane();
        runtime.PauseBeforeEvents = true;
        QueueCompletedTurn(runtime, "done");
        await pane.InitializeAsync();
        pane.DraftMessage = "visible now";

        var send = pane.SendAsync();
        await runtime.WaitForSendAsync();

        Assert.Contains(pane.Messages, message => message.Role == LeaderMessageRole.User && message.Text == "visible now");
        runtime.ReleaseSend();
        await send;
    }

    [Fact]
    public async Task Text_deltas_append_to_single_streaming_assistant_message()
    {
        var (pane, runtime, _) = CreatePane();
        runtime.QueueTurn(
            new AgentTextDelta("hello ", DateTimeOffset.UtcNow),
            new AgentTextDelta("world", DateTimeOffset.UtcNow),
            Completed());
        await pane.InitializeAsync();
        pane.DraftMessage = "go";

        await pane.SendAsync();

        var assistant = Assert.Single(pane.Messages, message => message.Role == LeaderMessageRole.Assistant);
        Assert.Equal("hello world", assistant.Text);
        Assert.False(assistant.IsStreaming);
    }

    [Fact]
    public async Task Turn_completed_clears_busy_state()
    {
        var (pane, runtime, _) = CreatePane();
        QueueCompletedTurn(runtime, "done");
        await pane.InitializeAsync();
        pane.DraftMessage = "go";

        await pane.SendAsync();

        Assert.False(pane.IsBusy);
    }

    [Fact]
    public async Task Concurrent_send_is_rejected_while_busy()
    {
        var (pane, runtime, _) = CreatePane();
        runtime.PauseBeforeEvents = true;
        QueueCompletedTurn(runtime, "done");
        await pane.InitializeAsync();
        pane.DraftMessage = "first";
        var first = pane.SendAsync();
        await runtime.WaitForSendAsync();

        pane.DraftMessage = "second";
        await Assert.ThrowsAsync<InvalidOperationException>(() => pane.SendAsync());

        runtime.ReleaseSend();
        await first;
    }

    [Fact]
    public async Task Failed_turn_clears_busy_state_and_surfaces_error()
    {
        var (pane, runtime, _) = CreatePane();
        runtime.SendException = new TimeoutException("provider detail");
        await pane.InitializeAsync();
        pane.DraftMessage = "keep me";

        await pane.SendAsync();

        Assert.False(pane.IsBusy);
        Assert.Contains(pane.Messages, message => message.Role == LeaderMessageRole.User && message.Text == "keep me");
        Assert.Contains(pane.Messages, message => message.Role == LeaderMessageRole.Error);
    }

    [Fact]
    public async Task Stream_ending_without_completion_surfaces_error()
    {
        var (pane, runtime, _) = CreatePane();
        runtime.QueueTurn(new AgentTextDelta("partial", DateTimeOffset.UtcNow));
        await pane.InitializeAsync();
        pane.DraftMessage = "go";

        await pane.SendAsync();

        Assert.False(pane.IsBusy);
        Assert.Contains(pane.Messages, message =>
            message.Role == LeaderMessageRole.Error &&
            message.Text.Contains("without completion", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Approval_event_is_presented_to_user()
    {
        var (pane, runtime, approval) = await StartApprovalTurnAsync();

        Assert.Same(approval, pane.PendingApproval);
        Assert.True(pane.HasPendingApproval);
        runtime.ReleaseApproval();
    }

    [Fact]
    public async Task Approval_options_are_rendered_from_runtime_request()
    {
        var (pane, runtime, approval) = await StartApprovalTurnAsync();

        Assert.Equal(approval.Options.Select(option => option.Label), pane.ApprovalOptions.Select(option => option.Label));
        runtime.ReleaseApproval();
    }

    [Fact]
    public async Task Approval_is_not_automatically_accepted()
    {
        var (_, runtime, _) = await StartApprovalTurnAsync();

        Assert.Empty(runtime.ApprovalDecisions);
        runtime.ReleaseApproval();
    }

    [Fact]
    public async Task Selecting_option_sends_matching_decision()
    {
        var (pane, runtime, approval) = await StartApprovalTurnAsync();
        var option = pane.ApprovalOptions[1];

        await pane.RespondToApprovalAsync(option);

        var decision = Assert.Single(runtime.ApprovalDecisions);
        Assert.Equal(approval.RequestId, decision.RequestId);
        Assert.Equal(approval.Options[1].Id, decision.OptionId);
    }

    [Fact]
    public async Task Approval_disappears_after_successful_response()
    {
        var (pane, _, _) = await StartApprovalTurnAsync();

        await pane.RespondToApprovalAsync(pane.ApprovalOptions[0]);

        Assert.False(pane.HasPendingApproval);
        Assert.Null(pane.PendingApproval);
    }

    [Fact]
    public async Task Failed_approval_response_remains_visible_with_error()
    {
        var (pane, runtime, _) = await StartApprovalTurnAsync();
        runtime.ApprovalException = new InvalidOperationException("provider detail");

        await pane.RespondToApprovalAsync(pane.ApprovalOptions[0]);

        Assert.True(pane.HasPendingApproval);
        Assert.NotNull(pane.ApprovalError);
        runtime.ApprovalException = null;
        runtime.ReleaseApproval();
    }

    [Fact]
    public async Task Approval_is_cleared_when_its_turn_terminates()
    {
        var (pane, runtime, _) = CreatePane();
        var approval = new AgentApprovalRequested(
            AgentApprovalRequestId.New(),
            AgentSessionId.New(),
            "Allow operation?",
            [new AgentApprovalOption("allow-once", "Allow once")],
            DateTimeOffset.UtcNow);
        runtime.QueueTurn(approval, Completed());
        await pane.InitializeAsync();
        pane.DraftMessage = "go";

        await pane.SendAsync();

        Assert.False(pane.HasPendingApproval);
        Assert.Empty(pane.ApprovalOptions);
    }

    [Fact]
    public async Task Stop_targets_current_leader_session()
    {
        var (pane, runtime, _) = CreatePane();
        runtime.PauseBeforeEvents = true;
        QueueCompletedTurn(runtime, "stopped");
        await pane.InitializeAsync();
        pane.DraftMessage = "go";
        var send = pane.SendAsync();
        await runtime.WaitForSendAsync();

        await pane.StopAsync();
        await send;

        Assert.Same(pane.Session, Assert.Single(runtime.StoppedSessions));
    }

    [Fact]
    public async Task Admission_instruction_is_present_in_the_same_sent_request_as_output_schema()
    {
        var (pane, runtime, _) = CreatePane();
        QueueCompletedTurn(runtime, "done");
        await pane.InitializeAsync();
        pane.DraftMessage = "Assess the architecture.";

        await pane.SendAsync();

        var request = Assert.Single(runtime.SentRequests);
        Assert.Contains("SPARSE DURABLE RATIONALE", request.Text, StringComparison.Ordinal);
        Assert.NotNull(request.OutputSchema);
        Assert.Contains("summary_deltas", request.OutputSchema, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admission_instruction_forbids_routine_facts_and_fabricated_sources()
    {
        var (pane, runtime, _) = CreatePane();
        QueueCompletedTurn(runtime, "done");
        await pane.InitializeAsync();
        pane.DraftMessage = "Continue.";

        await pane.SendAsync();

        var text = Assert.Single(runtime.SentRequests).Text;
        Assert.Contains("tests passed", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("build passed", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("files changed", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Worker PASS", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("routine tool output", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Fact, Note, Progress, Result, or Memory", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not fabricate", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source_refs = []", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_normal_leader_operation_uses_one_runtime_send()
    {
        var (pane, runtime, _) = CreatePane();
        QueueCompletedTurn(runtime, "done");
        await pane.InitializeAsync();
        pane.DraftMessage = "One operation.";

        await pane.SendAsync();

        Assert.Single(runtime.SentRequests);
        Assert.Single(runtime.SentSessions);
    }

    [Fact]
    public async Task Direct_user_text_and_summary_instruction_share_same_request()
    {
        var (pane, runtime, _) = CreatePane();
        QueueCompletedTurn(runtime, "done");
        await pane.InitializeAsync();
        pane.DraftMessage = "DIRECT USER MEANING MUST REMAIN";

        await pane.SendAsync();

        var request = Assert.Single(runtime.SentRequests);
        Assert.Contains("DIRECT USER MEANING MUST REMAIN", request.Text, StringComparison.Ordinal);
        Assert.Contains("If deletion does not materially increase future decision error, emit no Summary.", request.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Boot_generated_text_and_summary_instruction_share_same_request()
    {
        var runtime = new FakeAgentRuntime();
        var manager = new ProjectLeaderSessionManager();
        var pane = new LeaderPaneViewModel(
            CreateProject(),
            CreateRegistry(runtime),
            manager,
            () => Task.CompletedTask,
            bootContextBuilder: new TestBootContextBuilder());
        QueueCompletedTurn(runtime, "done");
        await pane.InitializeAsync();
        pane.DraftMessage = "BOOT USER MEANING MUST REMAIN";

        await pane.SendAsync();

        var request = Assert.Single(runtime.SentRequests);
        Assert.Contains("BOOT GENERATED CONTEXT", request.Text, StringComparison.Ordinal);
        Assert.Contains("BOOT USER MEANING MUST REMAIN", request.Text, StringComparison.Ordinal);
        Assert.Contains("SPARSE DURABLE RATIONALE", request.Text, StringComparison.Ordinal);
        Assert.Contains("summary_deltas", request.OutputSchema!, StringComparison.Ordinal);
    }

    private static (LeaderPaneViewModel Pane, FakeAgentRuntime Runtime, ProjectLeaderSessionManager Manager) CreatePane()
    {
        var runtime = new FakeAgentRuntime();
        var manager = new ProjectLeaderSessionManager();
        return (CreatePane(CreateProject(), CreateRegistry(runtime), manager), runtime, manager);
    }

    private static LeaderPaneViewModel CreatePane(
        CoreProject project,
        AgentRuntimeRegistry registry,
        ProjectLeaderSessionManager manager) =>
        new(project, registry, manager, () => Task.CompletedTask);

    private static AgentRuntimeRegistry CreateRegistry(params FakeAgentRuntime[] runtimes)
    {
        var registry = new AgentRuntimeRegistry();
        foreach (var runtime in runtimes)
        {
            registry.Register(runtime);
        }

        return registry;
    }

    private static CoreProject CreateProject(string rootPath = "C:/Games/Project") =>
        new(Guid.NewGuid(), "Project", rootPath, ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static void QueueCompletedTurn(FakeAgentRuntime runtime, string text) =>
        runtime.QueueTurn(new AgentTextDelta(text, DateTimeOffset.UtcNow), Completed(text));

    private static AgentTurnCompleted Completed(string? text = null) =>
        new(
            new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, text, null),
            DateTimeOffset.UtcNow);

    private static async Task<(LeaderPaneViewModel Pane, FakeAgentRuntime Runtime, AgentApprovalRequested Approval)> StartApprovalTurnAsync()
    {
        var (pane, runtime, _) = CreatePane();
        runtime.PauseAfterApproval = true;
        var approval = new AgentApprovalRequested(
            AgentApprovalRequestId.New(),
            AgentSessionId.New(),
            "Allow operation?",
            [
                new AgentApprovalOption("allow-once", "Allow once"),
                new AgentApprovalOption("decline", "Decline")
            ],
            DateTimeOffset.UtcNow);
        runtime.QueueTurn(approval, Completed());
        await pane.InitializeAsync();
        pane.DraftMessage = "go";
        _ = pane.SendAsync();
        await runtime.WaitForApprovalAsync();
        return (pane, runtime, approval);
    }

    private sealed class TestBootContextBuilder : ILeaderBootContextBuilder
    {
        public Task<AgentRequest> BuildAsync(
            CoreProject project,
            string originalUserText,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentRequest($"BOOT GENERATED CONTEXT\n{originalUserText}"));
    }
}
