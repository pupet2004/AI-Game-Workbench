using Workbench.App.Services;
using Workbench.App.Tests.Support;
using Workbench.App.ViewModels;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Runtime;

namespace Workbench.App.Tests;

public sealed class RuntimeCompositionTests
{
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
        Assert.Equal("Codex could not be started.", services.RuntimeUnavailableDetail);
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

        Assert.Contains("AppServices.CreateDefault(CodexRuntimeComposition.ConnectAsync)", source, StringComparison.Ordinal);
        Assert.Contains("window.Closing += async", source, StringComparison.Ordinal);
        Assert.Contains("await viewModel.DisposeAsync()", source, StringComparison.Ordinal);
        Assert.Contains("finally", source, StringComparison.Ordinal);
        Assert.DoesNotContain("token", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", source, StringComparison.OrdinalIgnoreCase);
    }
}
