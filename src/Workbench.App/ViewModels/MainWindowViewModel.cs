using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.Services;
using Workbench.App.Leader;
using Workbench.App.ViewModels.Leader;
using Workbench.Project.Opening;
using Workbench.Storage.Database;
using Workbench.App.ProjectWorld;
using Workbench.Core.Continuity;
using Workbench.App.AgentHost;
using Workbench.App.Views;
using Workbench.App.ViewModels.Panes;
using Workbench.Runtime.Agents;

namespace Workbench.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly AppServices _services;
    private readonly IFolderPickerService _folderPickerService;
    private readonly ProjectLeaderSessionManager _leaderSessions;
    private readonly LocalizationService _localization;
    private readonly Dictionary<AgentSessionId, WorkerSurfaceWindow> _workerSurfaceWindows = [];

    public MainWindowViewModel(
        AppServices services,
        IFolderPickerService folderPickerService,
        ProjectLeaderSessionManager? leaderSessions = null,
        LocalizationService? localization = null)
    {
        _services = services;
        _folderPickerService = folderPickerService;
        _leaderSessions = leaderSessions ?? new ProjectLeaderSessionManager(
            services.ProjectLeaderRepository,
            services.LeaderSessionEpochRepository,
            services.LeaderMessageRepository,
            services.TimeProvider);
        _localization = localization ?? new LocalizationService(services.WorkbenchSettingsRepository);
        _localization.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is "Item[]" or nameof(LocalizationService.Language))
            {
                OnPropertyChanged("Item[]");
            }
        };
        CurrentPage = CreateHome();
    }

    [ObservableProperty]
    public partial ViewModelBase CurrentPage { get; set; }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? startupArgs = null)
    {
        var home = (HomeViewModel)CurrentPage;
        try
        {
            await _services.InitializeAsync(cancellationToken);
            await _localization.InitializeAsync(cancellationToken);
            _localization.AdoptAsCurrent();
            await home.LoadAsync(cancellationToken);
            var startupProjectPath = startupArgs is null
                ? null
                : StartupArguments.TryGetProjectPath(startupArgs);
            if (startupProjectPath is not null)
            {
                await home.OpenPathAsync(startupProjectPath, cancellationToken);
            }
        }
        catch (DatabaseInitializationException)
        {
            home.SetStartupError(LocalizationService.Current["Home.DatabaseInitFailed"]);
        }
        catch (Exception)
        {
            home.SetStartupError(LocalizationService.Current["Home.StartFailed"]);
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
            ShowWorkspace,
            showSettings: ShowSettingsAsync,
            entryStatusService: _services.ProjectWorldEntryStatus,
            createProject: ShowNewProjectSetupAsync,
            localization: _localization);

    private async Task ShowNewProjectSetupAsync(ProjectOpenResult result)
    {
        var status = await _services.ProjectWorldEntryStatus.GetStatusAsync(result.Project);
        if (status.Kind == ProjectWorldEntryKind.ProjectWorldReady)
        {
            await ShowProjectOverviewAsync(result);
            return;
        }
        await ShowProjectSetupAsync(result, status);
    }

    private async Task ShowProjectSetupAsync(ProjectOpenResult result, ProjectWorldEntryStatus status)
    {
        ProjectWorldSetupViewModel? setup = null;
        async Task ReturnToSetupAsync()
        {
            CurrentPage = setup!;
            await setup!.InitializeAsync();
        }
        setup = new ProjectWorldSetupViewModel(_services, result, status,
            BackToHomeAsync, ShowWorkspace,
            () => ShowSettingsAsync(ReturnToSetupAsync, result.Project.Id));
        CurrentPage = setup;
        await setup.InitializeAsync();
    }

    public Task ShowSettingsAsync() => ShowSettingsAsync(BackToHomeAsync);

    private async Task ShowSettingsAsync(Func<Task> back, Guid? projectId = null)
    {
        if (CurrentPage is WorkspaceViewModel workspace)
        {
            await workspace.FlushLayoutAsync();
        }

        SettingsViewModel? settings = null;
        async Task ReturnToSettingsAsync()
        {
            if (settings is not null)
            {
                CurrentPage = settings;
                return;
            }

            await ShowSettingsAsync(back);
        }

        settings = new SettingsViewModel(
            _services.WorkbenchSettingsRepository,
            back,
            _localization,
            () => ShowDiagnosticsAsync(ReturnToSettingsAsync),
            _services.ProjectSettingsRepository,
            projectId,
            _services.RetryRuntimeAsync);
        CurrentPage = settings;
        await settings.InitializeAsync();
    }

    private async Task ShowDiagnosticsAsync(Func<Task> back)
    {
        var diagnostics = new DiagnosticsViewModel(_services, back, _localization);
        CurrentPage = diagnostics;
        await diagnostics.InitializeAsync();
    }

    [RelayCommand]
    private Task ShowSettings() => ShowSettingsAsync();

    private async Task ShowWorkspace(ProjectOpenResult result)
    {
        var entryStatus = await _services.ProjectWorldEntryStatus.GetStatusAsync(result.Project);
        if (entryStatus.Kind == ProjectWorldEntryKind.ProjectWorldReady)
        {
            await ShowProjectOverviewAsync(result);
            return;
        }

        // Preserve the existing Legacy/unmanaged workspace until the explicit
        // Project Home adoption/create confirmation flow is introduced. B1
        // initialization is entered only for an adopted Legacy project or an
        // already-governed empty Project World.
        if (entryStatus.Kind is ProjectWorldEntryKind.LegacySetupRequired
            or ProjectWorldEntryKind.LegacyWorkspaceReady
            or ProjectWorldEntryKind.ProjectWorldSetupIncomplete
            or ProjectWorldEntryKind.ProjectWorldReconfirmationRequired)
        {
            await ShowProjectSetupAsync(result, entryStatus);
            return;
        }

        if (entryStatus.Kind == ProjectWorldEntryKind.BootstrapRecoveryRequired)
        {
            await BackToHomeAsync();
            ((HomeViewModel)CurrentPage).SetStartupError(entryStatus.DisplayLabel);
            return;
        }

        await ShowThreeColumnWorkspaceAsync(result);
    }

    private async Task ShowThreeColumnWorkspaceAsync(ProjectOpenResult result)
    {
        WorkspaceViewModel? workspace = null;
        async Task ReturnToWorkspaceAsync()
        {
            if (workspace is not null)
            {
                await workspace.LeaderPane.RefreshRotationSettingsAsync();
                await workspace.LibraryPane.RefreshRotationSettingsAsync();
                CurrentPage = workspace;
                return;
            }

            await ShowProjectOverviewAsync(result);
        }

        workspace = new WorkspaceViewModel(
            result,
            _services.ProjectLayoutRepository,
            BackToHomeAsync,
            runtimeRegistry: _services.RuntimeRegistry,
            leaderSessionManager: _leaderSessions,
            runtimeUnavailableDetail: _services.RuntimeUnavailableDetail,
            reconnectRuntime: _services.RetryRuntimeAsync,
            releaseRuntimeForExternalCli: _services.ReleaseRuntimeForExternalSessionAsync,
            restoreRuntimeAfterExternalCli: _services.RestoreRuntimeAfterExternalSessionAsync,
            projectSettingsRepository: _services.ProjectSettingsRepository,
            rotationStateService: new LeaderSessionRotationStateService(
                _services.WorkbenchSettingsRepository,
                _services.ProjectSettingsRepository,
                _services.LeaderSessionEpochRepository,
                _services.TimeProvider),
            rolloverService: _services.LeaderSessionRolloverService,
            epochRepository: _services.LeaderSessionEpochRepository,
            messageRepository: _services.LeaderMessageRepository,
            bootContextBuilder: _services.LeaderBootContextBuilder,
            memoryPolicyCoordinator: _services.LeaderMemoryPolicyCoordinator,
            taskRepository: _services.TaskRepository,
            taskRevisionRepository: _services.TaskRevisionRepository,
            workerSessionRouter: _services.WorkerSessionRouter,
            workerRoutingStore: _services.WorkerRoutingStore,
            projectLibraryEvolutionRepository: _services.ProjectLibraryEvolutionRepository,
             projectMemoryApi: _services.ProjectMemoryApi,
             responseBinder: _services.LeaderReviewUserResponseBinder,
             projectSummaryRepository: _services.ProjectSummaryRepository,
             evolutionCandidateRepository: _services.ProjectEvolutionCandidateRepository,
             acceptedStateReader: _services.LibraryAcceptedStateReader,
             agentHost: _services.AgentHost,
             workerExecutionRepository: _services.WorkerExecutionRepository,
             canonicalWorkerLaunch: _services.CanonicalWorkerLaunch,
             confirmedWorkerDraftLaunch: _services.ConfirmedWorkerDraftLaunch,
             authorityRepository: _services.B1AuthorityRepository,
             workerExecutionBridge: _services.B1WorkerExecutionBridge,
             canonicalWorkerCompletions: _services.CanonicalWorkerCompletions,
             openGuidedDecision: _ => ShowProjectReviewAsync(result, ReturnToWorkspaceAsync),
             openHostedSurface: OpenHostedWorkerSurfaceAsync,
             openProjectOverview: () => ShowProjectOverviewAsync(result),
             openProjectReview: () => ShowProjectReviewAsync(result, ReturnToWorkspaceAsync),
             openSettings: () => ShowSettingsAsync(ReturnToWorkspaceAsync, result.Project.Id),
             openProjectHistory: () => ShowProjectHistoryAsync(result, ReturnToWorkspaceAsync),
             acceptAuthorityConfirmation: AcceptAuthorityConfirmationAsync);
        CurrentPage = workspace;
        // Load Worker recovery before exposing the Leader draft confirmation.
        // Otherwise a fast user click can start an execution while the Work
        // pane is still reconciling startup state and classify that fresh
        // execution as an interrupted pre-existing run.
        await workspace.WorkPane.LoadAsync(result.Project.Id);
        await workspace.LeaderPane.InitializeAsync();
        await _services.CanonicalWorkerCompletionBridge.RecoverAsync(
            result.Project.Id,
            _services.UserPrincipalProvider.GetCurrent());
        await _services.LeaderReviewOrchestrator.RecoverAsync(result.Project.Id);
        await _services.LeaderReviewAutoProceed.RecoverAsync(result.Project.Id);
        await _services.LeaderReviewAskUserGate.RecoverAsync(result.Project.Id);
        await workspace.LibraryPane.InitializeAsync();
    }

    private async Task ShowProjectOverviewAsync(ProjectOpenResult result)
    {
        var entryStatus = await _services.ProjectWorldEntryStatus.GetStatusAsync(result.Project);
        if (entryStatus.Kind != ProjectWorldEntryKind.ProjectWorldReady)
        {
            await ShowProjectSetupAsync(result, entryStatus);
            return;
        }

        ProjectWorldExplorerViewModel? explorer = null;
        async Task ReturnToProjectOverviewAsync()
        {
            if (explorer is not null)
            {
                CurrentPage = explorer;
                await explorer.InitializeAsync();
                return;
            }

            await ShowProjectOverviewAsync(result);
        }

        explorer = new ProjectWorldExplorerViewModel(
            _services,
            result,
            BackToHomeAsync,
            assignmentRef => ShowManualWorkAsync(result, assignmentRef),
            () => ShowThreeColumnWorkspaceAsync(result),
            () => ShowProjectReviewAsync(result),
            handoffRef => ShowGuidedDecisionAsync(result, handoffRef),
            () => ShowSettingsAsync(ReturnToProjectOverviewAsync, result.Project.Id),
            () => ShowProjectHistoryAsync(result, ReturnToProjectOverviewAsync));
        CurrentPage = explorer;
        await explorer.InitializeAsync();
    }

    private async Task ShowProjectHistoryAsync(ProjectOpenResult result, Func<Task> back)
    {
        var history = new ProjectHistoryViewModel(_services, result, back, _localization);
        CurrentPage = history;
        await history.InitializeAsync();
    }

    private async Task ShowProjectReviewAsync(
        ProjectOpenResult result,
        Func<Task>? backToWorkspace = null)
    {
        var review = new ProjectReviewViewModel(
            _services,
            result,
            () => ShowProjectOverviewAsync(result),
            backToWorkspace ?? (() => ShowWorkspace(result)),
            handoffRef => ShowGuidedDecisionAsync(
                result,
                handoffRef,
                () => ShowProjectReviewAsync(result, backToWorkspace)));
        CurrentPage = review;
        await review.InitializeAsync();
    }

    private Task<AuthorityDecision> AcceptAuthorityConfirmationAsync(
        AuthorityConfirmationDraft draft,
        CancellationToken cancellationToken)
    {
        var principal = new UserPrincipalRef(_services.UserPrincipalProvider.GetCurrent().Value);
        return _services.B1AuthorityCommands.AuthorAcceptedStateAsync(
            new AuthorAcceptedStateCommand(
                new ProjectRef(draft.ProjectId),
                principal,
                new DecidingAuthorityRef.UserPrincipal(principal),
                draft.ConsideredRefs,
                draft.Contributions),
            cancellationToken);
    }

    private async Task ShowManualWorkAsync(ProjectOpenResult result, AssignmentRef assignmentRef)
    {
        var manualWork = new ManualWorkViewModel(
            _services,
            result,
            assignmentRef,
            () => ShowWorkspace(result),
            _ => ShowProjectReviewAsync(result));
        CurrentPage = manualWork;
        await manualWork.InitializeAsync();
    }

    private async Task OpenHostedWorkerSurfaceAsync(WorkerSessionCardViewModel worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        if (_workerSurfaceWindows.TryGetValue(worker.Session.Id, out var existing))
        {
            if (!existing.IsVisible)
                existing.Show();
            existing.WindowState = Avalonia.Controls.WindowState.Normal;
            // Activate can be ignored by Windows when another window owns the
            // foreground. A brief Topmost toggle reliably brings this surface
            // forward without leaving it permanently above other apps.
            existing.Topmost = true;
            existing.Activate();
            existing.Topmost = false;
            return;
        }

        var surface = new HostedAgentSurfaceViewModel(_services.AgentHost, worker.Session);
        var window = new WorkerSurfaceWindow(surface)
        {
            Title = $"Worker · {worker.TaskTitle}"
        };
        _workerSurfaceWindows[worker.Session.Id] = window;
        window.Closed += (_, _) =>
        {
            _workerSurfaceWindows.Remove(worker.Session.Id);
            _ = surface.DisposeAsync();
        };
        window.Show();
        await surface.HydrateTranscriptAsync();
    }

    private async Task ShowGuidedDecisionAsync(
        ProjectOpenResult result,
        HandoffRef handoffRef,
        Func<Task>? afterCommit = null)
    {
        var decision = new GuidedDecisionViewModel(
            _services,
            result,
            handoffRef,
            back: () => ShowProjectOverviewAsync(result),
            afterCommit: afterCommit ?? (() => ShowProjectOverviewAsync(result)));
        CurrentPage = decision;
        await decision.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var window in _workerSurfaceWindows.Values.ToArray())
        {
            window.Close();
        }
        _workerSurfaceWindows.Clear();
        if (CurrentPage is WorkspaceViewModel workspace)
        {
            await workspace.FlushLayoutAsync();
        }

        await _services.DisposeAsync();
    }
}
