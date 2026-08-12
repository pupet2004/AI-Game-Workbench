using CommunityToolkit.Mvvm.ComponentModel;
using Workbench.App.Services;
using Workbench.App.ViewModels.Leader;
using Workbench.Project.Opening;
using Workbench.Storage.Database;

namespace Workbench.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly AppServices _services;
    private readonly IFolderPickerService _folderPickerService;
    private readonly ProjectLeaderSessionManager _leaderSessions;

    public MainWindowViewModel(
        AppServices services,
        IFolderPickerService folderPickerService,
        ProjectLeaderSessionManager? leaderSessions = null)
    {
        _services = services;
        _folderPickerService = folderPickerService;
        _leaderSessions = leaderSessions ?? new ProjectLeaderSessionManager(
            services.ProjectLeaderRepository,
            services.LeaderSessionEpochRepository,
            services.LeaderMessageRepository,
            TimeProvider.System);
        CurrentPage = CreateHome();
    }

    [ObservableProperty]
    public partial ViewModelBase CurrentPage { get; set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var home = (HomeViewModel)CurrentPage;
        try
        {
            await _services.InitializeAsync(cancellationToken);
            await home.LoadAsync(cancellationToken);
        }
        catch (DatabaseInitializationException)
        {
            home.SetStartupError("Could not initialize the local Workbench database. Your existing data was not changed.");
        }
        catch (Exception)
        {
            home.SetStartupError("Could not start AI Game Workbench.");
        }
    }

    public async Task BackToHomeAsync()
    {
        if (CurrentPage is WorkspaceViewModel workspace)
        {
            await workspace.FlushLayoutAsync();
        }

        CurrentPage = CreateHome();
        await ((HomeViewModel)CurrentPage).LoadAsync();
    }

    private HomeViewModel CreateHome() =>
        new(
            _services.ProjectRepository,
            _services.ProjectOpenService,
            _folderPickerService,
            ShowWorkspace);

    private async Task ShowWorkspace(ProjectOpenResult result)
    {
        var workspace = new WorkspaceViewModel(
            result,
            _services.ProjectLayoutRepository,
            BackToHomeAsync,
            runtimeRegistry: _services.RuntimeRegistry,
            leaderSessionManager: _leaderSessions,
            runtimeUnavailableDetail: _services.RuntimeUnavailableDetail,
            reconnectRuntime: _services.RetryRuntimeAsync);
        CurrentPage = workspace;
        await workspace.LeaderPane.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (CurrentPage is WorkspaceViewModel workspace)
        {
            await workspace.FlushLayoutAsync();
        }

        await _services.DisposeAsync();
    }
}
