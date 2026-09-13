using Workbench.App.Tests.Support;
using Workbench.App.ProjectWorld;
using Workbench.App.ViewModels;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Microsoft.Data.Sqlite;
using Workbench.Core.Leaders;

namespace Workbench.App.Tests;

public sealed class NavigationTests
{
    [Fact]
    public async Task Home_settings_returns_to_projects()
    {
        await using var context = await AppTestContext.CreateAsync();
        var main = context.CreateMain();
        await main.InitializeAsync();

        await ((HomeViewModel)main.CurrentPage).ShowSettingsCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentPage);
        await settings.BackCommand.ExecuteAsync(null);

        Assert.IsType<HomeViewModel>(main.CurrentPage);
    }

    [Fact]
    public async Task Project_overview_settings_returns_to_same_overview()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await AppTestContext.CreateAsync(folder.Path);
        var main = context.CreateMain();
        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).CreateProjectAsync();
        var setup = Assert.IsType<ProjectWorldSetupViewModel>(main.CurrentPage);
        await setup.EstablishGovernanceCommand.ExecuteAsync(null);
        await setup.PreviewInitializationCommand.ExecuteAsync(null);
        await setup.ConfirmInitializationCommand.ExecuteAsync(null);

        var overview = Assert.IsType<ProjectWorldExplorerViewModel>(main.CurrentPage);
        await overview.OpenSettingsCommand.ExecuteAsync(null);
        Assert.IsType<SettingsViewModel>(main.CurrentPage);
        await ((SettingsViewModel)main.CurrentPage).BackCommand.ExecuteAsync(null);

        Assert.Same(overview, main.CurrentPage);
    }

    [Fact]
    public async Task Workspace_settings_returns_to_same_workspace_with_draft_session_and_layout()
    {
        using var folder = new TemporaryDirectory();
        var runtime = new FakeAgentRuntime();
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: RegistryWith(runtime));
        var main = context.CreateMain();
        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(folder.Path);
        var workspace = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);
        QueueCompleted(runtime, "answer");
        workspace.LeaderPane.DraftMessage = "question";
        await workspace.LeaderPane.SendAsync();
        workspace.LeaderPane.DraftMessage = "Unsent follow-up";
        workspace.ApplyPaneWidths(400, 400, 200);
        var layout = workspace.Layout;
        var session = workspace.LeaderPane.Session;

        for (var visit = 0; visit < 2; visit++)
        {
            await workspace.OpenSettingsPageCommand.ExecuteAsync(null);
            var settings = Assert.IsType<SettingsViewModel>(main.CurrentPage);
            Assert.Equal(layout, await context.Services.ProjectLayoutRepository.GetAsync(workspace.Result.Project.Id));
            await settings.BackCommand.ExecuteAsync(null);

            Assert.Same(workspace, main.CurrentPage);
            Assert.Same(session, workspace.LeaderPane.Session);
            Assert.Equal("Unsent follow-up", workspace.LeaderPane.DraftMessage);
            Assert.Equal(layout, workspace.Layout);
            Assert.Equal(["question", "answer"], workspace.LeaderPane.Messages.Select(message => message.Text));
        }

        Assert.Single(runtime.CreatedSessions);
    }

    [Theory]
    [InlineData(null, LeaderSessionRotationPolicy.ManualOnly)]
    [InlineData(LeaderSessionRotationPolicy.Ask, LeaderSessionRotationPolicy.Ask)]
    public async Task Returning_from_settings_refreshes_rotation_policy_and_preserves_project_override(
        LeaderSessionRotationPolicy? projectOverride,
        LeaderSessionRotationPolicy expectedPolicy)
    {
        using var folder = new TemporaryDirectory();
        await using var context = await AppTestContext.CreateAsync();
        var main = context.CreateMain();
        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(folder.Path);
        var workspace = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);
        await workspace.LibraryPane.SetLeaderSessionRotationPolicyOverrideAsync(projectOverride);

        await workspace.OpenSettingsPageCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentPage);
        await settings.SetLeaderSessionRotationPolicyAsync(LeaderSessionRotationPolicy.ManualOnly);
        Assert.Equal(
            LeaderSessionRotationPolicy.ManualOnly,
            await context.Services.WorkbenchSettingsRepository.GetLeaderSessionRotationPolicyAsync());
        await settings.BackCommand.ExecuteAsync(null);

        Assert.Same(workspace, main.CurrentPage);
        Assert.Equal(expectedPolicy, workspace.LibraryPane.EffectiveRotationPolicy);
        Assert.Equal(projectOverride, workspace.LibraryPane.RotationPolicyOverride);
    }

    [Fact]
    public async Task App_starts_on_home()
    {
        await using var context = await AppTestContext.CreateAsync();
        var main = context.CreateMain();
        await main.InitializeAsync();

        Assert.IsType<HomeViewModel>(main.CurrentPage);
    }

    [Fact]
    public async Task Successful_project_open_navigates_to_workspace()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await AppTestContext.CreateAsync();
        var main = context.CreateMain();
        await main.InitializeAsync();

        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(folder.Path);

        Assert.IsType<WorkspaceViewModel>(main.CurrentPage);
    }

    [Fact]
    public async Task Production_workspace_composition_shows_draft_confirmation_for_a_leader_proposal()
    {
        using var folder = new TemporaryDirectory();
        var runtime = new FakeAgentRuntime();
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: RegistryWith(runtime));
        var main = context.CreateMain();
        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(folder.Path);
        var workspace = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(
                AgentSessionId.New(),
                AgentSessionStatus.Completed,
                "{\"response\":\"Draft ready.\",\"draft_proposal\":{\"title\":\"Read smoke context\",\"goal\":\"Read the smoke context file\",\"scope\":\"Read one file\",\"outOfScope\":\"Do not modify files\",\"acceptance\":[\"Report the marker\"],\"riskLevel\":\"Low\",\"recommendedExecutionProfile\":{\"providerHint\":\"fake-provider\",\"modelHint\":\"model-a\",\"runtimeHint\":\"fake-runtime\"}}}",
                null),
            DateTimeOffset.UtcNow));
        workspace.LeaderPane.DraftMessage = "Delegate the smoke context read.";

        await workspace.LeaderPane.SendAsync();

        var task = Assert.Single(await context.Services.TaskRepository.ListAsync(workspace.Result.Project.Id));
        Assert.Single(await context.Services.TaskRevisionRepository.ListAsync(workspace.Result.Project.Id, task.TaskId));
        Assert.NotNull(workspace.LeaderPane.DraftConfirmation);
        Assert.True(workspace.LeaderPane.HasDraftConfirmation);
        await using var connection = context.Services.Database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM worker_executions WHERE project_id = $projectId";
        command.Parameters.AddWithValue("$projectId", workspace.Result.Project.Id.ToString());
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Production_workspace_composition_confirms_a_draft_into_one_visible_worker_and_consumes_the_draft()
    {
        using var folder = new TemporaryDirectory();
        var runtime = new FakeAgentRuntime();
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: RegistryWith(runtime));
        var main = context.CreateMain();
        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(folder.Path);
        var workspace = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(
                AgentSessionId.New(),
                AgentSessionStatus.Completed,
                "{\"response\":\"Draft ready.\",\"draft_proposal\":{\"title\":\"Read smoke context\",\"goal\":\"Read the smoke context file\",\"scope\":\"Read one file\",\"outOfScope\":\"Do not modify files\",\"acceptance\":[\"Report the marker\"],\"riskLevel\":\"Low\",\"recommendedExecutionProfile\":{\"providerHint\":\"fake-provider\",\"modelHint\":\"model-a\",\"runtimeHint\":\"fake-runtime\"}}}",
                null),
            DateTimeOffset.UtcNow));
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, "Worker completed.", null),
            DateTimeOffset.UtcNow));
        workspace.LeaderPane.DraftMessage = "Delegate the smoke context read.";

        await workspace.LeaderPane.SendAsync();
        Assert.True(workspace.LeaderPane.HasDraftConfirmation);

        await workspace.LeaderPane.ConfirmDraftCommand.ExecuteAsync(null);

        Assert.Equal(2, runtime.CreatedSessions.Count);
        Assert.Equal("model-a", runtime.CreatedSessions.Last().ModelId);
        var workerRequest = Assert.Single(runtime.SentRequests,
            request => request.Text.StartsWith("WORKBENCH WORKER TASK CONTRACT", StringComparison.Ordinal));
        Assert.Contains("# Workbench Worker", workerRequest.Text, StringComparison.Ordinal);
        Assert.Contains("TASK GOAL", workerRequest.Text, StringComparison.Ordinal);
        Assert.Contains("Read the smoke context file", workerRequest.Text, StringComparison.Ordinal);
        Assert.Contains("IN SCOPE", workerRequest.Text, StringComparison.Ordinal);
        Assert.Contains("OUT OF SCOPE", workerRequest.Text, StringComparison.Ordinal);
        Assert.Contains("ACCEPTANCE CRITERIA", workerRequest.Text, StringComparison.Ordinal);
        Assert.Contains("already confirmed and started this Worker", workerRequest.Text, StringComparison.Ordinal);
        var sessions = await context.Services.WorkerRoutingStore.ListSessionsAsync(workspace.Result.Project.Id);
        var session = Assert.Single(sessions);
        Assert.Equal("model-a", session.Profile.ModelProfileId);
        Assert.Single(workspace.WorkPane.Workers);
        Assert.Null(workspace.LeaderPane.DraftConfirmation);
        Assert.False(workspace.LeaderPane.HasDraftConfirmation);
        var events = await context.Services.WorkerRoutingStore.ListSessionsAsync(workspace.Result.Project.Id);
        Assert.Single(events);
        var epochId = Assert.IsType<Guid>(workspace.LeaderPane.SessionEpochId);
        var messages = await context.Services.LeaderMessageRepository.GetAllAsync(epochId);
        var report = Assert.Single(messages, message => message.Text.Contains("Worker Session:", StringComparison.Ordinal));
        Assert.Contains("Worker completed.", report.Text, StringComparison.Ordinal);
        Assert.Contains(workspace.LeaderPane.Messages, message => message.Text == report.Text);
        Assert.Empty(await context.Services.ProjectMemoryApi.GetPendingLibraryProposalsAsync(workspace.Result.Project.Id));

        var reloaded = new WorkspaceViewModel(
            workspace.Result,
            context.Services.ProjectLayoutRepository,
            () => Task.CompletedTask,
            context.Time,
            runtimeRegistry: context.Services.RuntimeRegistry,
            leaderSessionManager: new Workbench.App.ViewModels.Leader.ProjectLeaderSessionManager(
                context.Services.ProjectLeaderRepository,
                context.Services.LeaderSessionEpochRepository,
                context.Services.LeaderMessageRepository,
                context.Time),
            taskRepository: context.Services.TaskRepository,
            taskRevisionRepository: context.Services.TaskRevisionRepository,
            workerSessionRouter: context.Services.WorkerSessionRouter,
            workerRoutingStore: context.Services.WorkerRoutingStore);
        await reloaded.LeaderPane.InitializeAsync();
        await reloaded.WorkPane.LoadAsync(workspace.Result.Project.Id);
        Assert.Equal("Completed", Assert.Single(reloaded.WorkPane.Workers).Status);
        Assert.Contains(reloaded.LeaderPane.Messages, message => message.Text == report.Text);

        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.LeaderPane.ConfirmDraftAsync());
        Assert.Single(await context.Services.WorkerRoutingStore.ListSessionsAsync(workspace.Result.Project.Id));
        Assert.Equal(2, runtime.CreatedSessions.Count);
        await using var connection = context.Services.Database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM task_events WHERE project_id = $projectId AND event_type = 'WorkerSessionStarted'";
        command.Parameters.AddWithValue("$projectId", workspace.Result.Project.Id.ToString());
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Failed_worker_start_keeps_the_draft_confirmation_for_retry()
    {
        using var folder = new TemporaryDirectory();
        var runtime = new FakeAgentRuntime();
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: RegistryWith(runtime));
        var main = context.CreateMain();
        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(folder.Path);
        var workspace = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);
        runtime.QueueTurn(new AgentTurnCompleted(
            new AgentResult(
                AgentSessionId.New(),
                AgentSessionStatus.Completed,
                "{\"response\":\"Draft ready.\",\"draft_proposal\":{\"title\":\"Read smoke context\",\"goal\":\"Read the smoke context file\",\"scope\":\"Read one file\",\"outOfScope\":\"Do not modify files\",\"acceptance\":[\"Report the marker\"],\"riskLevel\":\"Low\",\"recommendedExecutionProfile\":{\"providerHint\":\"fake-provider\",\"modelHint\":\"model-a\",\"runtimeHint\":\"fake-runtime\"}}}",
                null),
            DateTimeOffset.UtcNow));
        workspace.LeaderPane.DraftMessage = "Delegate the smoke context read.";
        await workspace.LeaderPane.SendAsync();
        runtime.CreateException = new InvalidOperationException("worker runtime unavailable");

        await workspace.LeaderPane.ConfirmDraftAsync();

        Assert.True(workspace.LeaderPane.HasDraftConfirmation);
        Assert.NotNull(workspace.LeaderPane.DraftConfirmation);
        Assert.Empty(workspace.WorkPane.Workers);
        Assert.Empty(await context.Services.WorkerRoutingStore.ListSessionsAsync(workspace.Result.Project.Id));
        Assert.Contains(workspace.LeaderPane.Messages, message =>
            message.Text.Contains("Worker could not be started", StringComparison.OrdinalIgnoreCase) ||
            message.Text.Contains("无法启动 Worker", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Back_to_projects_refreshes_recent_list()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await AppTestContext.CreateAsync();
        var main = context.CreateMain();
        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(folder.Path);

        await main.BackToHomeAsync();

        var home = Assert.IsType<HomeViewModel>(main.CurrentPage);
        Assert.Equal(Path.GetFullPath(folder.Path), Assert.Single(home.RecentProjects).RootPath);
    }

    [Fact]
    public async Task Back_to_projects_preserves_leader_state_in_memory()
    {
        using var folder = new TemporaryDirectory();
        var runtime = new FakeAgentRuntime();
        var registry = RegistryWith(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var main = context.CreateMain();
        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(folder.Path);
        var workspace = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);
        QueueCompleted(runtime, "answer");
        workspace.LeaderPane.DraftMessage = "question";
        await workspace.LeaderPane.SendAsync();

        await main.BackToHomeAsync();

        Assert.Equal(1, context.LeaderSessions.Count);
        Assert.IsType<HomeViewModel>(main.CurrentPage);
    }

    [Fact]
    public async Task Reopening_project_restores_conversation()
    {
        using var folder = new TemporaryDirectory();
        var runtime = new FakeAgentRuntime();
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: RegistryWith(runtime));
        var main = context.CreateMain();
        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(folder.Path);
        var first = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);
        QueueCompleted(runtime, "answer");
        first.LeaderPane.DraftMessage = "question";
        await first.LeaderPane.SendAsync();
        await main.BackToHomeAsync();

        var home = Assert.IsType<HomeViewModel>(main.CurrentPage);
        await home.OpenRecentProjectAsync(Assert.Single(home.RecentProjects));
        var reopened = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);

        Assert.Equal(["question", "answer"], reopened.LeaderPane.Messages.Select(message => message.Text));
        Assert.Same(first.LeaderPane.Session, reopened.LeaderPane.Session);
    }

    [Fact]
    public async Task Opening_second_project_has_empty_independent_leader()
    {
        using var firstFolder = new TemporaryDirectory();
        using var secondFolder = new TemporaryDirectory();
        var runtime = new FakeAgentRuntime();
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: RegistryWith(runtime));
        var main = context.CreateMain();
        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(firstFolder.Path);
        var first = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);
        QueueCompleted(runtime, "answer-a");
        first.LeaderPane.DraftMessage = "question-a";
        await first.LeaderPane.SendAsync();
        await main.BackToHomeAsync();

        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(secondFolder.Path);
        var second = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);

        Assert.Empty(second.LeaderPane.Messages);
        Assert.Null(second.LeaderPane.Session);
        await main.BackToHomeAsync();
        var firstRecent = ((HomeViewModel)main.CurrentPage).RecentProjects.Single(item => item.RootPath == Path.GetFullPath(firstFolder.Path));
        await ((HomeViewModel)main.CurrentPage).OpenRecentProjectAsync(firstRecent);
        Assert.Equal(["question-a", "answer-a"], ((WorkspaceViewModel)main.CurrentPage).LeaderPane.Messages.Select(message => message.Text));
    }

    private static AgentRuntimeRegistry RegistryWith(FakeAgentRuntime runtime)
    {
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        return registry;
    }

    private static void QueueCompleted(FakeAgentRuntime runtime, string text)
    {
        var now = DateTimeOffset.UtcNow;
        runtime.QueueTurn(
            new AgentTextDelta(text, now),
            new AgentTurnCompleted(
                new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, text, null),
                now));
    }
}
