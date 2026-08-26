using Workbench.App.Services;
using Workbench.App.Tests.Support;
using Workbench.App.ViewModels;
using Workbench.App.ViewModels.Leader;
using Workbench.App.ViewModels.Panes;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Registry;
using Workbench.Runtime.Runtime;
using Workbench.Core.Layout;
using Workbench.Core.Projects;
using Workbench.Project.Git;
using Workbench.Project.Opening;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class RuntimeCompositionTests
{
    [Fact]
    public void AppServices_owns_one_project_summary_repository()
    {
        using var directory = new TemporaryDirectory("summary-composition");
        var services = AppServices.CreateForDatabasePath(Path.Combine(directory.Path, "workbench.db"));

        Assert.Same(services.ProjectSummaryRepository, services.ProjectSummaryRepository);
        Assert.Same(
            services.ProjectSummaryRepository,
            services.LeaderSummaryRecoveryService.SummaryRepository);
    }

    [Fact]
    public async Task AppServices_runs_summary_recovery_after_database_initialization()
    {
        using var directory = new TemporaryDirectory("summary-initialize");
        var services = AppServices.CreateForDatabasePath(Path.Combine(directory.Path, "workbench.db"));
        await services.Database.InitializeAsync();
        var pending = await SeedPendingSummaryAsync(services, directory.Path);

        await services.InitializeAsync();

        var summary = Assert.Single(await services.ProjectSummaryRepository.QueryAsync(new SummaryQuery(pending.ProjectId, 20)));
        Assert.Equal(pending.ResultId, summary.ResultId);
        Assert.Empty(await services.LeaderMessageRepository.GetPendingSummaryResultsAsync());
        await services.DisposeAsync();
    }

    [Fact]
    public async Task Recovery_runs_before_normal_UI_boot_or_workspace_operation()
    {
        using var directory = new TemporaryDirectory("summary-before-ui");
        var services = AppServices.CreateForDatabasePath(Path.Combine(directory.Path, "workbench.db"));
        await services.Database.InitializeAsync();
        var pending = await SeedPendingSummaryAsync(services, directory.Path);
        var main = new MainWindowViewModel(services, new TestFolderPickerService(null));

        await main.InitializeAsync();

        Assert.IsType<HomeViewModel>(main.CurrentPage);
        Assert.Single(await services.ProjectSummaryRepository.QueryAsync(new SummaryQuery(pending.ProjectId, 20)));
        Assert.Empty(services.RuntimeRegistry.Runtimes);
        await main.DisposeAsync();
    }

    [Fact]
    public async Task Provider_unavailable_does_not_block_summary_recovery()
    {
        using var directory = new TemporaryDirectory("summary-offline");
        var services = AppServices.CreateForDatabasePath(
            Path.Combine(directory.Path, "workbench.db"),
            runtimeRegistry: new AgentRuntimeRegistry());
        await services.Database.InitializeAsync();
        var pending = await SeedPendingSummaryAsync(services, directory.Path);

        await services.InitializeAsync();

        Assert.Empty(services.RuntimeRegistry.Runtimes);
        Assert.Single(await services.ProjectSummaryRepository.QueryAsync(new SummaryQuery(pending.ProjectId, 20)));
        await services.DisposeAsync();
    }

    [Fact]
    public async Task Configured_agent_runtime_is_not_started_until_enabled_in_settings()
    {
        using var directory = new TemporaryDirectory("configured-runtime");
        var runtime = new FakeAgentRuntime();
        var connectionCount = 0;
        var services = AppServices.CreateForDatabasePath(
            Path.Combine(directory.Path, "workbench.db"),
            configuredRuntimeFactories:
            [
                new ConfiguredAgentRuntimeFactory(
                    "opencode",
                    (_, _) =>
                    {
                        connectionCount++;
                        return Task.FromResult<IAgentRuntime>(runtime);
                    })
            ]);
        await services.InitializeAsync();

        await services.RetryRuntimeAsync();
        Assert.Equal(0, connectionCount);
        Assert.Empty(services.RuntimeRegistry.Runtimes);

        await services.WorkbenchSettingsRepository.SaveAgentRuntimeSettingsAsync(
            new Workbench.Storage.Settings.AgentRuntimeSettings("opencode", true, "C:\\Tools\\opencode.cmd"));
        await services.RetryRuntimeAsync();

        Assert.Equal(1, connectionCount);
        Assert.Same(runtime, Assert.Single(services.RuntimeRegistry.Runtimes));
        await services.DisposeAsync();
    }

    [Fact]
    public async Task Workspace_receives_the_same_summary_repository_instance()
    {
        using var directory = new TemporaryDirectory("summary-workspace");
        var services = AppServices.CreateForDatabasePath(Path.Combine(directory.Path, "workbench.db"));
        await services.InitializeAsync();
        var now = services.TimeProvider.GetUtcNow();
        var project = new CoreProject(Guid.NewGuid(), "Project", directory.Path, ProjectType.Generic, null, now, now);
        await services.ProjectRepository.UpsertAsync(project);
        var result = new ProjectOpenResult(
            project,
            ProjectLayout.CreateDefault(project.Id),
            new GitSnapshot(true, false, null, null, null, false, false, null));

        var workspace = new WorkspaceViewModel(
            result,
            services.ProjectLayoutRepository,
            () => Task.CompletedTask,
            projectSummaryRepository: services.ProjectSummaryRepository);

        Assert.Same(services.ProjectSummaryRepository, workspace.LeaderPane.SummaryRepository);
        await workspace.DisposeAsync();
        await services.DisposeAsync();
    }

    [Fact]
    public async Task LeaderPane_receives_real_summary_repository()
    {
        using var directory = new TemporaryDirectory("summary-main-window");
        using var projectDirectory = new TemporaryDirectory("summary-main-project");
        var services = AppServices.CreateForDatabasePath(Path.Combine(directory.Path, "workbench.db"));
        await services.Database.InitializeAsync();
        await services.ProjectOpenService.OpenAsync(projectDirectory.Path);
        var main = new MainWindowViewModel(services, new TestFolderPickerService(null));
        await main.InitializeAsync();
        var home = Assert.IsType<HomeViewModel>(main.CurrentPage);

        await home.OpenRecentProjectAsync(Assert.Single(home.RecentProjects));

        var workspace = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);
        Assert.Same(services.ProjectSummaryRepository, workspace.LeaderPane.SummaryRepository);
        await main.DisposeAsync();
    }

    [Fact]
    public async Task No_pending_summary_does_not_change_boot_context()
    {
        using var directory = new TemporaryDirectory("summary-noop-boot");
        var services = AppServices.CreateForDatabasePath(Path.Combine(directory.Path, "workbench.db"));
        await services.Database.InitializeAsync();
        var now = services.TimeProvider.GetUtcNow();
        var project = new CoreProject(Guid.NewGuid(), "Project", directory.Path, ProjectType.Generic, null, now, now);
        await services.ProjectRepository.UpsertAsync(project);
        var epoch = CreateEpoch(project, now);
        await services.ProjectLeaderRepository.CreateCurrentEpochAsync(
            new StoredProjectLeader(project.Id, null, now, now),
            epoch);

        await services.InitializeAsync();

        Assert.Null((await services.LeaderSessionEpochRepository.GetAsync(epoch.Id))!.BootContextDeliveredAt);
        Assert.Empty(await services.LeaderMessageRepository.GetPendingSummaryResultsAsync());
        await services.DisposeAsync();
    }

    [Fact]
    public async Task Opening_a_project_with_a_persisted_leader_session_connects_its_runtime_without_retry()
    {
        using var directory = new TemporaryDirectory("runtime-restore");
        using var projectDirectory = new TemporaryDirectory("runtime-restore-project");
        var accountId = ProviderAccountId.New();
        var firstRuntime = new FakeAgentRuntime(
            accountId: accountId,
            models: [new ModelProfile(new ProviderId("fake-provider"), "model-a", "Model A", AgentCapability.Resume)]);
        var firstRegistry = new AgentRuntimeRegistry();
        firstRegistry.Register(firstRuntime);
        var firstServices = AppServices.CreateForDatabasePath(
            Path.Combine(directory.Path, "workbench.db"),
            runtimeRegistry: firstRegistry);
        await firstServices.InitializeAsync();
        var opened = await firstServices.ProjectOpenService.OpenAsync(projectDirectory.Path);
        var firstPane = new LeaderPaneViewModel(
            opened.Project,
            firstRegistry,
            new ProjectLeaderSessionManager(
                firstServices.ProjectLeaderRepository,
                firstServices.LeaderSessionEpochRepository,
                firstServices.LeaderMessageRepository),
            () => Task.CompletedTask);
        await firstPane.InitializeAsync();
        firstPane.DraftMessage = "Persist this Leader session.";
        await firstPane.SendAsync();
        var originalSession = firstPane.Session!;
        var originalEpoch = firstPane.SessionEpochId;
        await firstServices.DisposeAsync();

        var restoredRuntime = new FakeAgentRuntime(
            accountId: accountId,
            models: [new ModelProfile(new ProviderId("fake-provider"), "model-a", "Model A", AgentCapability.Resume)]);
        var restoredServices = AppServices.CreateForDatabasePath(
            Path.Combine(directory.Path, "workbench.db"),
            runtimeFactory: _ => Task.FromResult<IAgentRuntime>(restoredRuntime));
        var main = new MainWindowViewModel(restoredServices, new TestFolderPickerService(null));
        await main.InitializeAsync();
        var home = Assert.IsType<HomeViewModel>(main.CurrentPage);

        await home.OpenRecentProjectAsync(Assert.Single(home.RecentProjects));

        var workspace = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);
        var restoredSession = workspace.LeaderPane.Session!;
        Assert.Same(restoredRuntime, Assert.Single(restoredServices.RuntimeRegistry.Runtimes));
        Assert.True(workspace.LeaderPane.IsRuntimeAvailable);
        Assert.False(workspace.LeaderPane.CanRetryRuntime);
        Assert.Equal(originalEpoch, workspace.LeaderPane.SessionEpochId);
        Assert.Equal(originalSession.Id, restoredSession.Id);
        Assert.Equal(originalSession.ExternalSessionId, restoredSession.ExternalSessionId);
        Assert.Empty(restoredRuntime.CreatedSessions);
        Assert.Empty(restoredRuntime.ResumedSessions);
        await main.DisposeAsync();
    }

    [Fact]
    public void Local_codex_account_id_is_stable_across_composition_instances()
    {
        var first = CodexRuntimeComposition.CreateLocalAccountSummary();
        var second = CodexRuntimeComposition.CreateLocalAccountSummary();

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(new ProviderId("codex"), first.ProviderId);
        Assert.Equal("Local Codex Account", first.DisplayName);
    }

    [Fact]
    public void Codex_composition_builds_one_app_server_configuration_without_credentials()
    {
        var environment = new Dictionary<string, string?>
        {
            ["WORKBENCH_CODEX_EXECUTABLE"] = "C:/Tools/node.exe",
            ["WORKBENCH_CODEX_ENTRY"] = "C:/Tools/codex.js",
            ["WORKBENCH_CODEX_CWD"] = "C:/Workbench/App"
        };

        var options = CodexRuntimeComposition.CreateOptions(
            name => environment.GetValueOrDefault(name),
            "C:/Program Files",
            "C:/Users/Test/AppData/Roaming",
            "C:/AppBase");

        Assert.Equal("C:/Tools/node.exe", options.ExecutablePath);
        Assert.Equal(["C:/Tools/codex.js", "app-server", "--stdio"], options.Arguments);
        Assert.Equal("C:/Workbench/App", options.WorkingDirectory);
    }

    [Fact]
    public void Codex_composition_can_use_the_standalone_cli_installation()
    {
        var environment = new Dictionary<string, string?>
        {
            ["CODEX_CLI_PATH"] = "C:/Tools/codex.exe",
            ["WORKBENCH_CODEX_CWD"] = "C:/Workbench/App"
        };

        var options = CodexRuntimeComposition.CreateStandaloneOptions(
            name => environment.GetValueOrDefault(name),
            "C:/Users/Test/AppData/Local",
            "C:/AppBase");

        Assert.Equal("C:/Tools/codex.exe", options.ExecutablePath);
        Assert.Equal(["app-server", "--stdio"], options.Arguments);
        Assert.Equal("C:/Workbench/App", options.WorkingDirectory);
    }

    [Fact]
    public void OpenCode_composition_builds_an_acp_configuration_without_credentials()
    {
        var environment = new Dictionary<string, string?>
        {
            ["WORKBENCH_OPENCODE_EXECUTABLE"] = "C:/Tools/opencode.cmd",
            ["WORKBENCH_OPENCODE_CWD"] = "C:/Workbench/App"
        };

        var options = OpenCodeRuntimeComposition.CreateOptions(
            name => environment.GetValueOrDefault(name),
            "C:/Users/Test/AppData/Roaming",
            "C:/AppBase");

        Assert.Equal("C:/Tools/opencode.cmd", options.ExecutablePath);
        Assert.Equal("C:/Workbench/App", options.WorkingDirectory);
    }

    [Fact]
    public async Task App_services_initialize_storage_without_starting_runtime_then_connects_on_demand()
    {
        using var directory = new TemporaryDirectory("runtime-composition");
        var runtime = new FakeAgentRuntime();
        var services = AppServices.CreateForDatabasePath(
            Path.Combine(directory.Path, "workbench.db"),
            runtimeFactory: _ => Task.FromResult<IAgentRuntime>(runtime));

        await services.InitializeAsync();

        Assert.Empty(services.RuntimeRegistry.Runtimes);
        await services.RetryRuntimeAsync();
        Assert.Same(runtime, Assert.Single(services.RuntimeRegistry.Runtimes));
        await services.DisposeAsync();
        Assert.True(runtime.IsDisposed);
    }

    [Fact]
    public async Task Runtime_startup_failure_keeps_home_available()
    {
        using var directory = new TemporaryDirectory("runtime-failure");
        var services = AppServices.CreateForDatabasePath(
            Path.Combine(directory.Path, "workbench.db"),
            runtimeFactory: _ => Task.FromException<IAgentRuntime>(new InvalidOperationException("credential detail")));
        var main = new MainWindowViewModel(services, new TestFolderPickerService(null));

        await main.InitializeAsync();

        Assert.IsType<HomeViewModel>(main.CurrentPage);
        Assert.Empty(services.RuntimeRegistry.Runtimes);
        Assert.Null(services.RuntimeUnavailableDetail);
        await services.RetryRuntimeAsync();
        Assert.Equal("An Agent runtime could not be started.", services.RuntimeUnavailableDetail);
        await main.DisposeAsync();
    }

    [Fact]
    public async Task Runtime_startup_failure_can_be_retried_in_process()
    {
        using var directory = new TemporaryDirectory("runtime-retry");
        var runtime = new FakeAgentRuntime();
        var attempts = 0;
        var services = AppServices.CreateForDatabasePath(
            Path.Combine(directory.Path, "workbench.db"),
            runtimeFactory: _ => ++attempts == 1
                ? Task.FromException<IAgentRuntime>(new InvalidOperationException("startup failed"))
                : Task.FromResult<IAgentRuntime>(runtime));
        await services.InitializeAsync();
        await services.RetryRuntimeAsync();

        await services.RetryRuntimeAsync();

        Assert.Equal(2, attempts);
        Assert.Same(runtime, Assert.Single(services.RuntimeRegistry.Runtimes));
        Assert.Null(services.RuntimeUnavailableDetail);
        await services.DisposeAsync();
    }

    [Fact]
    public async Task One_runtime_can_connect_when_another_runtime_is_unavailable()
    {
        using var directory = new TemporaryDirectory("runtime-partial-availability");
        var available = new FakeAgentRuntime(providerName: "OpenCode");
        var services = AppServices.CreateForDatabasePath(
            Path.Combine(directory.Path, "workbench.db"),
            runtimeFactory: _ => Task.FromException<IAgentRuntime>(new InvalidOperationException("Codex unavailable")),
            additionalRuntimeFactories: [_ => Task.FromResult<IAgentRuntime>(available)]);
        await services.InitializeAsync();

        await services.RetryRuntimeAsync();

        Assert.Same(available, Assert.Single(services.RuntimeRegistry.Runtimes));
        Assert.Null(services.RuntimeUnavailableDetail);
        await services.DisposeAsync();
    }

    [Fact]
    public async Task Concurrent_runtime_retries_share_one_connection_attempt()
    {
        using var directory = new TemporaryDirectory("runtime-single-flight");
        var runtime = new FakeAgentRuntime();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var services = AppServices.CreateForDatabasePath(
            Path.Combine(directory.Path, "workbench.db"),
            runtimeFactory: async _ =>
            {
                Interlocked.Increment(ref attempts);
                started.TrySetResult();
                await release.Task;
                return runtime;
            });
        await services.Database.InitializeAsync();

        var first = services.RetryRuntimeAsync();
        await started.Task;
        var second = services.RetryRuntimeAsync();
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, attempts);
        Assert.Same(runtime, Assert.Single(services.RuntimeRegistry.Runtimes));
        await services.DisposeAsync();
    }

    [Fact]
    public async Task Shutdown_disposes_runtime_that_finishes_connecting_late()
    {
        using var directory = new TemporaryDirectory("runtime-shutdown-race");
        var runtime = new FakeAgentRuntime();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = AppServices.CreateForDatabasePath(
            Path.Combine(directory.Path, "workbench.db"),
            runtimeFactory: async _ =>
            {
                started.SetResult();
                await release.Task;
                return runtime;
            });
        await services.Database.InitializeAsync();
        var connect = services.RetryRuntimeAsync();
        await started.Task;

        var dispose = services.DisposeAsync().AsTask();
        release.SetResult();
        await Task.WhenAll(connect, dispose);

        Assert.True(runtime.IsDisposed);
        Assert.Empty(services.RuntimeRegistry.Runtimes);
    }

    [Fact]
    public async Task Runtime_startup_cancellation_is_not_reported_as_unavailable()
    {
        using var directory = new TemporaryDirectory("runtime-cancellation");
        using var cancellation = new CancellationTokenSource();
        var services = AppServices.CreateForDatabasePath(
            Path.Combine(directory.Path, "workbench.db"),
            runtimeFactory: _ =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<IAgentRuntime>(cancellation.Token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => services.RetryRuntimeAsync(cancellation.Token));
        Assert.Null(services.RuntimeUnavailableDetail);
    }

    [Fact]
    public void App_root_owns_codex_factory_and_graceful_shutdown()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var source = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Workbench.App", "App.axaml.cs"));

        Assert.Contains("CodexRuntimeComposition.ConnectAsync", source, StringComparison.Ordinal);
        Assert.Contains("OpenCodeRuntimeComposition.ConnectAsync", source, StringComparison.Ordinal);
        Assert.Contains("window.Closing += async", source, StringComparison.Ordinal);
        Assert.Contains("await viewModel.DisposeAsync()", source, StringComparison.Ordinal);
        Assert.Contains("finally", source, StringComparison.Ordinal);
        Assert.DoesNotContain("token", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", source, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<PendingLeaderSummaryResult> SeedPendingSummaryAsync(
        AppServices services,
        string rootPath)
    {
        var now = services.TimeProvider.GetUtcNow();
        var project = new CoreProject(Guid.NewGuid(), "Project", rootPath, ProjectType.Generic, null, now, now);
        await services.ProjectRepository.UpsertAsync(project);
        var epoch = CreateEpoch(project, now);
        await services.ProjectLeaderRepository.CreateCurrentEpochAsync(
            new StoredProjectLeader(project.Id, null, now, now),
            epoch);
        var resultId = Guid.NewGuid();
        var payload = "[{\"occurred_at\":\"2026-08-20T10:15:30+00:00\",\"kind\":\"Decision\",\"text\":\"Recovered.\",\"source_refs\":[]}]";
        var message = await services.LeaderMessageRepository.AppendAsync(
            epoch.Id,
            "assistant",
            "Visible.",
            now,
            metadata: new LeaderResultMetadata(resultId, payload));
        return new PendingLeaderSummaryResult(message.Id, project.Id, epoch.Id, resultId, payload, now);
    }

    private static StoredLeaderSessionEpoch CreateEpoch(CoreProject project, DateTimeOffset now) =>
        new(
            Guid.NewGuid(),
            project.Id,
            "provider",
            Guid.NewGuid(),
            "model",
            Guid.NewGuid(),
            "external-session",
            project.RootPath,
            now,
            now,
            null,
            null,
            null);
}
