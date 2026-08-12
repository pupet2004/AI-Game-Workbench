using Workbench.App.Services;
using Workbench.App.ViewModels;
using Workbench.App.ViewModels.Leader;
using Workbench.Project.Git;
using Workbench.Project.Opening;
using Workbench.App.Tests.Support;
using Workbench.Runtime.Registry;
using Workbench.App.Leader;

namespace Workbench.App.Tests;

internal sealed class AppTestContext : IAsyncDisposable
{
    private readonly TemporaryDirectory _directory;
    private readonly TestFolderPickerService _folderPicker;

    private AppTestContext(
        TemporaryDirectory directory,
        AppServices services,
        MutableTimeProvider time,
        TestFolderPickerService folderPicker,
        ProjectLeaderSessionManager leaderSessions)
    {
        _directory = directory;
        Services = services;
        Time = time;
        _folderPicker = folderPicker;
        LeaderSessions = leaderSessions;
    }

    public AppServices Services { get; }

    public string DatabasePath => Path.Combine(_directory.Path, "workbench.db");

    public MutableTimeProvider Time { get; }

    public ProjectLeaderSessionManager LeaderSessions { get; }

    public ProjectOpenResult? LastOpened { get; private set; }

    public static async Task<AppTestContext> CreateAsync(
        string? folderPath = "unused",
        AgentRuntimeRegistry? runtimeRegistry = null)
    {
        var directory = new TemporaryDirectory("database");
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00"));
        var services = AppServices.CreateForDatabasePath(
            Path.Combine(directory.Path, "workbench.db"),
            time,
            runtimeRegistry);
        await services.InitializeAsync();
        return new AppTestContext(
            directory,
            services,
            time,
            new TestFolderPickerService(folderPath),
            new ProjectLeaderSessionManager(
                services.ProjectLeaderRepository,
                services.LeaderSessionEpochRepository,
                services.LeaderMessageRepository,
                time));
    }

    public HomeViewModel CreateHome(Func<string, CancellationToken, Task<ProjectOpenResult>>? opener = null) =>
        new(Services.ProjectRepository, Services.ProjectOpenService, _folderPicker, result =>
        {
            LastOpened = result;
            return Task.CompletedTask;
        }, opener);

    public MainWindowViewModel CreateMain() => new(Services, _folderPicker, LeaderSessions);

    public SettingsViewModel CreateSettings() => new(Services.WorkbenchSettingsRepository, () => Task.CompletedTask);

    public LeaderSessionRotationStateService CreateRotationStateService(TimeZoneInfo timeZone) =>
        new(Services.WorkbenchSettingsRepository, Services.ProjectSettingsRepository, Services.LeaderSessionEpochRepository, Time, timeZone);

    public WorkspaceViewModel CreateWorkspace(ProjectOpenResult result, TimeSpan? debounce = null) =>
        new(
            result,
            Services.ProjectLayoutRepository,
            () => Task.CompletedTask,
            Time,
            debounce,
            Services.RuntimeRegistry,
            LeaderSessions,
            Services.RuntimeUnavailableDetail,
            projectSettingsRepository: Services.ProjectSettingsRepository,
            rotationStateService: CreateRotationStateService(TimeZoneInfo.Utc),
            rolloverService: Services.LeaderSessionRolloverService);

    public async Task<WorkspaceViewModel> CreateWorkspaceForNewProjectAsync(TimeSpan? debounce = null)
    {
        using var folder = new TemporaryDirectory();
        return CreateWorkspace(await Services.ProjectOpenService.OpenAsync(folder.Path), debounce);
    }

    public ValueTask DisposeAsync()
    {
        _directory.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class TestFolderPickerService(string? folderPath) : IFolderPickerService
{
    public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult(folderPath);
}

internal sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
}
