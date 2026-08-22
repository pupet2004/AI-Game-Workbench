using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.ViewModels.Panes;
using Workbench.App.ViewModels.Leader;
using Workbench.Core.Layout;
using Workbench.Project.Opening;
using Workbench.Runtime.Registry;
using Workbench.Storage.Projects;
using Workbench.Storage.Settings;
using Workbench.App.Leader;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;
using Workbench.Storage.Tasks;
using Workbench.App.Worker;
using Workbench.App.Memory;

namespace Workbench.App.ViewModels;

public partial class WorkspaceViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly ProjectLayoutRepository _layoutRepository;
    private readonly Func<Task> _backToProjects;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _debounce;
    private CancellationTokenSource? _saveCancellation;

    public WorkspaceViewModel(
        ProjectOpenResult result,
        ProjectLayoutRepository layoutRepository,
        Func<Task> backToProjects,
        TimeProvider? timeProvider = null,
        TimeSpan? debounce = null,
        AgentRuntimeRegistry? runtimeRegistry = null,
        ProjectLeaderSessionManager? leaderSessionManager = null,
        string? runtimeUnavailableDetail = null,
        Func<CancellationToken, Task>? reconnectRuntime = null,
        ProjectSettingsRepository? projectSettingsRepository = null,
        LeaderSessionRotationStateService? rotationStateService = null,
        LeaderSessionRolloverService? rolloverService = null,
        LeaderSessionEpochRepository? epochRepository = null,
        LeaderMessageRepository? messageRepository = null,
        ProjectMemoryService? projectMemoryService = null,
        ProjectMemorySynthesisRepository? memorySynthesisRepository = null,
        ILeaderBootContextBuilder? bootContextBuilder = null,
        LeaderMemoryPolicyCoordinator? memoryPolicyCoordinator = null,
        TaskRepository? taskRepository = null,
        TaskRevisionRepository? taskRevisionRepository = null,
        WorkerSessionRouter? workerSessionRouter = null,
        IWorkerRoutingStore? workerRoutingStore = null,
        ProjectLibraryRepository? projectLibraryRepository = null,
        ProjectLibraryEvolutionRepository? projectLibraryEvolutionRepository = null,
         IProjectMemoryApi? projectMemoryApi = null,
         ILeaderReviewUserResponseBinder? responseBinder = null,
         ProjectSummaryRepository? projectSummaryRepository = null)
    {
        Result = result;
        _layoutRepository = layoutRepository;
        _backToProjects = backToProjects;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(350);
        Layout = result.Layout;
        WorkPane = new WorkPaneViewModel(
            FocusWorkAsync,
            workerRoutingStore,
            runtimeRegistry,
            new CodexInteractiveSessionLauncher());
        LibraryPane = new LibraryPaneViewModel(
            result,
            FocusLibraryAsync,
            projectSettingsRepository,
            rotationStateService,
            projectMemoryService,
            memorySynthesisRepository,
            epochRepository,
            library: projectLibraryRepository,
            evolutionLibrary: projectLibraryEvolutionRepository,
            projectMemoryApi: projectMemoryApi);
        LeaderPane = new LeaderPaneViewModel(
            result.Project,
            runtimeRegistry ?? new AgentRuntimeRegistry(),
            leaderSessionManager ?? new ProjectLeaderSessionManager(),
            FocusLeaderAsync,
            runtimeUnavailableDetail,
            reconnectRuntime,
            rotationStateService,
            rolloverService,
            epochRepository,
            messageRepository,
            bootContextBuilder,
            memoryPolicyCoordinator,
            taskRepository: taskRepository,
            taskRevisionRepository: taskRevisionRepository,
            workerSessionRouter: workerSessionRouter,
            refreshWorkPane: cancellationToken => WorkPane.LoadAsync(result.Project.Id, cancellationToken),
            projectMemoryApi: projectMemoryApi,
            timeProvider: _timeProvider,
            refreshLibraryPane: LibraryPane.LoadLibraryAsync,
            responseBinder: responseBinder,
            git: result.Git,
            projectSummaryRepository: projectSummaryRepository);
    }

    public ProjectOpenResult Result { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeaderRatio), nameof(WorkRatio), nameof(LibraryRatio), nameof(LeaderGridLength), nameof(WorkGridLength), nameof(LibraryGridLength))]
    public partial ProjectLayout Layout { get; set; }

    public LeaderPaneViewModel LeaderPane { get; }

    public WorkPaneViewModel WorkPane { get; }

    public LibraryPaneViewModel LibraryPane { get; }

    public double LeaderRatio => Layout.LeaderWidth;

    public double WorkRatio => Layout.WorkWidth;

    public double LibraryRatio => Layout.LibraryWidth;

    public GridLength LeaderGridLength => new(LeaderRatio, GridUnitType.Star);

    public GridLength WorkGridLength => new(WorkRatio, GridUnitType.Star);

    public GridLength LibraryGridLength => new(LibraryRatio, GridUnitType.Star);

    public void ApplyPaneWidths(double leaderPixels, double workPixels, double libraryPixels)
    {
        var ratios = WorkspaceLayoutCalculator.Normalize(leaderPixels, workPixels, libraryPixels);
        Layout = new ProjectLayout(Layout.ProjectId, ratios.Leader, ratios.Work, ratios.Library, Layout.FocusedPane, _timeProvider.GetUtcNow());
        ScheduleSave();
    }

    public Task FocusLeaderAsync() => SetAndSaveAsync(ProjectLayout.FocusLeader(Layout.ProjectId));

    public Task FocusWorkAsync() => SetAndSaveAsync(ProjectLayout.FocusWork(Layout.ProjectId));

    public Task FocusLibraryAsync() => SetAndSaveAsync(ProjectLayout.FocusLibrary(Layout.ProjectId));

    public Task ResetLayoutAsync() => SetAndSaveAsync(ProjectLayout.CreateDefault(Layout.ProjectId));

    public Task FlushLayoutAsync()
    {
        CancelPendingSave();
        return _layoutRepository.SaveAsync(Layout);
    }

    [RelayCommand]
    private Task FocusLeader() => FocusLeaderAsync();

    [RelayCommand]
    private Task FocusWork() => FocusWorkAsync();

    [RelayCommand]
    private Task FocusLibrary() => FocusLibraryAsync();

    [RelayCommand]
    private Task ResetLayout() => ResetLayoutAsync();

    [RelayCommand]
    private Task BackToProjects() => _backToProjects();

    public async ValueTask DisposeAsync()
    {
        await FlushLayoutAsync();
        _saveCancellation?.Dispose();
    }

    private async Task SetAndSaveAsync(ProjectLayout layout)
    {
        Layout = layout;
        await FlushLayoutAsync();
    }

    private void ScheduleSave()
    {
        CancelPendingSave();
        var cancellation = new CancellationTokenSource();
        _saveCancellation = cancellation;
        _ = SaveAfterDelayAsync(cancellation.Token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_debounce, cancellationToken);
            await _layoutRepository.SaveAsync(Layout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelPendingSave()
    {
        _saveCancellation?.Cancel();
        _saveCancellation?.Dispose();
        _saveCancellation = null;
    }
}
