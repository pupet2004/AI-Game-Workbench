using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.ProjectWorld;
using Workbench.App.Services;
using Workbench.Core.Continuity;
using Workbench.Core.Projects;
using Workbench.Project.Opening;

namespace Workbench.App.ViewModels;

public sealed partial class ProjectWorldSetupViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private ProjectOpenResult _openResult;
    private readonly Func<Task> _backToHome;
    private readonly Func<ProjectOpenResult, Task> _openWorkspace;
    private readonly Func<Task>? _configureAgent;
    private ProjectWorldInitializationRequest? _previewRequest;
    private string[] _availableModels = [];
    private bool _databaseReady;
    private bool _checksCompleted;
    private string? _godotExecutablePath;

    public ProjectWorldSetupViewModel(
        AppServices services,
        ProjectOpenResult openResult,
        ProjectWorldEntryStatus status,
        Func<Task> backToHome,
        Func<ProjectOpenResult, Task> openWorkspace,
        Func<Task>? configureAgent = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _openResult = openResult ?? throw new ArgumentNullException(nameof(openResult));
        Status = status ?? throw new ArgumentNullException(nameof(status));
        _backToHome = backToHome ?? throw new ArgumentNullException(nameof(backToHome));
        _openWorkspace = openWorkspace ?? throw new ArgumentNullException(nameof(openWorkspace));
        _configureAgent = configureAgent;
        UserPrincipal = _services.UserPrincipalProvider.GetCurrent().Value;
        ResponsibilityObligation = LocalizationService.Current["ProjectSetup.DefaultResponsibility"];
        ResponsibilityExpectedOutcome = LocalizationService.Current["ProjectSetup.DefaultExpected"];
        InitialAssignment = LocalizationService.Current["ProjectSetup.DefaultAssignment"];
        IsGovernanceEstablished = status.Kind is ProjectWorldEntryKind.ProjectWorldReady or ProjectWorldEntryKind.ProjectWorldSetupIncomplete;
    }

    public ProjectWorldEntryStatus Status { get; }
    public string ProjectName => _openResult.Project.Name;
    public string ProjectPath => _openResult.Project.RootPath;
    public string ProjectDetectionText =>
        _openResult.Project.Type == ProjectType.Godot &&
        File.Exists(Path.Combine(_openResult.Project.RootPath, "project.godot"))
            ? LocalizationService.Current["FirstRun.GodotDetected"]
            : _openResult.Project.Type.ToString();
    public bool IsGodotProject => _openResult.Project.Type == ProjectType.Godot;
    public string GodotStatusText => !_checksCompleted
        ? LocalizationService.Current["FirstRun.NotChecked"]
        : !IsGodotProject
            ? LocalizationService.Current["FirstRun.NotApplicable"]
            : _godotExecutablePath is null
                ? LocalizationService.Current["FirstRun.GodotUnavailable"]
                : LocalizationService.Current["FirstRun.GodotReady"];
    public string GodotDetailText => _godotExecutablePath is null
        ? LocalizationService.Current["FirstRun.GodotUnavailableDetail"]
        : _godotExecutablePath;
    public string GitStatusText
    {
        get
        {
            if (!_checksCompleted)
                return LocalizationService.Current["FirstRun.NotChecked"];
            if (!_openResult.Git.GitInstalled)
                return LocalizationService.Current["FirstRun.GitUnavailable"];
            if (!string.IsNullOrWhiteSpace(_openResult.Git.Error))
                return LocalizationService.Current["FirstRun.GitError"];
            if (_openResult.Git.IsDirty)
                return LocalizationService.Current["FirstRun.GitDirty"];
            return _openResult.Git.IsRepository
                ? LocalizationService.Current["FirstRun.GitReady"]
                : LocalizationService.Current["FirstRun.GitNotConfigured"];
        }
    }
    public string DatabaseStatusText => LocalizationService.Current[_databaseReady
        ? "FirstRun.DatabaseReady" : "FirstRun.NotChecked"];
    public bool IsAgentReady => _availableModels.Length > 0;
    public string AgentStatusText => !_checksCompleted
        ? LocalizationService.Current["FirstRun.NotChecked"] : IsAgentReady
        ? LocalizationService.Current["FirstRun.AgentReady"]
        : LocalizationService.Current["FirstRun.AgentNotReady"];
    public string AgentDetailText
    {
        get
        {
            if (IsAgentReady)
            {
                return string.Join(Environment.NewLine, _availableModels);
            }

            return LocalizationService.Current["FirstRun.AgentNotReadyDetail"];
        }
    }
    public string ProjectStateText => LocalizationService.Current["FirstRun.NoAcceptedState"];
    public string PrimaryActionText =>
        !IsGovernanceEstablished
            ? LocalizationService.Current["FirstRun.StartProject"]
            : IsPreviewVisible
                ? LocalizationService.Current["ProjectSetup.ConfirmOpen"]
                : LocalizationService.Current["ProjectSetup.Preview"];
    public bool CanConfigureAgent => !IsBusy && _configureAgent is not null;
    public bool CanStartProject => !IsBusy && (IsGovernanceEstablished ? CanPreview : CanEstablishGovernance);
    public ObservableCollection<RoleKind> AvailableRoleKinds { get; } =
        new(Enum.GetValues<RoleKind>());

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreview))]
    public partial string UserPrincipal { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreview))]
    public partial RoleKind SelectedRoleKind { get; set; } = RoleKind.Worker;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreview))]
    public partial string ResponsibilityObligation { get; set; } = "Own the first project responsibility";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreview))]
    public partial string ResponsibilityExpectedOutcome { get; set; } = "A clear first project outcome";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreview))]
    public partial string InitialAssignment { get; set; } = "Define the first bounded piece of work";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreview), nameof(CanEstablishGovernance), nameof(CanStartProject), nameof(PrimaryActionText))]
    public partial bool IsGovernanceEstablished { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm), nameof(PrimaryActionText))]
    public partial bool IsPreviewVisible { get; set; }

    [ObservableProperty]
    public partial string? PreviewText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreview), nameof(CanConfirm), nameof(CanEstablishGovernance), nameof(CanStartProject), nameof(CanConfigureAgent))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public bool CanPreview => !IsBusy && IsGovernanceEstablished &&
        !string.IsNullOrWhiteSpace(UserPrincipal) &&
        !string.IsNullOrWhiteSpace(ResponsibilityObligation) &&
        !string.IsNullOrWhiteSpace(ResponsibilityExpectedOutcome) &&
        !string.IsNullOrWhiteSpace(InitialAssignment);

    public bool CanConfirm => CanPreview && IsPreviewVisible && _previewRequest == BuildRequest();

    public bool CanEstablishGovernance => !IsBusy && !IsGovernanceEstablished &&
        !string.IsNullOrWhiteSpace(UserPrincipal) &&
        Status.Kind is ProjectWorldEntryKind.UnmanagedProjectUnavailable or ProjectWorldEntryKind.LegacySetupRequired;

    partial void OnUserPrincipalChanged(string value) => InvalidatePreview();
    partial void OnSelectedRoleKindChanged(RoleKind value) => InvalidatePreview();
    partial void OnResponsibilityObligationChanged(string value) => InvalidatePreview();
    partial void OnResponsibilityExpectedOutcomeChanged(string value) => InvalidatePreview();
    partial void OnInitialAssignmentChanged(string value) => InvalidatePreview();

    private void InvalidatePreview()
    {
        _previewRequest = null;
        IsPreviewVisible = false;
        PreviewText = null;
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(CanStartProject));
        OnPropertyChanged(nameof(CanEstablishGovernance));
    }

    [RelayCommand]
    private async Task EstablishGovernanceAsync()
    {
        if (!CanEstablishGovernance)
            return;

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var projectRef = new ProjectRef(_openResult.Project.Id);
            var principal = new UserPrincipalRef(UserPrincipal);
            if (Status.Kind == ProjectWorldEntryKind.LegacySetupRequired)
            {
                await _services.B1ProjectGovernance.AdoptLegacyProjectAsync(
                    projectRef, principal, _services.TimeProvider.GetUtcNow());
            }
            else
            {
                await _services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(
                    projectRef, principal);
            }

            IsGovernanceEstablished = true;
            OnPropertyChanged(nameof(CanEstablishGovernance));
            OnPropertyChanged(nameof(PrimaryActionText));
        }
        catch (Exception)
        {
            ErrorMessage = LocalizationService.Current["FirstRun.SetupFailed"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PreviewInitializationAsync()
    {
        if (!CanPreview)
            return;

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var request = BuildRequest();
            var preview = await _services.ProjectWorldInitialization.PreviewAsync(request);
            if (request != BuildRequest())
                return;
            _previewRequest = request;
            PreviewText = $"{preview.InitialAssignment}\n{preview.ResponsibilityExpectedOutcome}\n{preview.ResponsibilityObligation}\n{preview.UserPrincipalRef.Value}";
            IsPreviewVisible = true;
            OnPropertyChanged(nameof(PrimaryActionText));
        }
        catch (Exception)
        {
            InvalidatePreview();
            ErrorMessage = LocalizationService.Current["FirstRun.PreviewFailed"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmInitializationAsync()
    {
        if (!CanConfirm)
            return;

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _services.ProjectWorldInitialization.CommitAsync(_previewRequest!);
            InvalidatePreview();
            await _openWorkspace(_openResult);
        }
        catch (Exception)
        {
            InvalidatePreview();
            ErrorMessage = LocalizationService.Current["FirstRun.SetupFailed"];
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task BackAsync() => _backToHome();

    [RelayCommand]
    private Task StartProjectAsync()
    {
        if (!CanStartProject)
            return Task.CompletedTask;
        if (!IsGovernanceEstablished)
            return EstablishGovernanceAsync();
        return IsPreviewVisible
            ? ConfirmInitializationAsync()
            : PreviewInitializationAsync();
    }

    public Task InitializeAsync() => RetryAgentAsync();

    [RelayCommand]
    private Task ConfigureAgentAsync() => CanConfigureAgent ? _configureAgent!() : Task.CompletedTask;

    [RelayCommand]
    private async Task RetryAgentAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        ErrorMessage = null;
        _availableModels = [];
        _databaseReady = false;
        _godotExecutablePath = null;
        _checksCompleted = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            _openResult = await _services.ProjectOpenService.OpenAsync(ProjectPath, timeout.Token);
            _databaseReady = true;
            var configuredGodotPath = await _services.WorkbenchSettingsRepository.GetGodotExecutablePathAsync(timeout.Token);
            _godotExecutablePath = IsGodotProject
                ? GodotExecutableResolver.Resolve(configuredGodotPath)
                : null;
            await _services.RetryRuntimeAsync(timeout.Token);
            var resources = await _services.RuntimeRegistry.GetWorkerResourcesAsync(timeout.Token);
            _availableModels = resources.Select(resource => $"{resource.DisplayLabel} · {resource.ModelProfileId}").ToArray();
        }
        catch (Exception)
        {
            ErrorMessage = LocalizationService.Current["FirstRun.CheckFailed"];
        }
        finally
        {
            _checksCompleted = true;
            IsBusy = false;
            OnPropertyChanged(nameof(IsAgentReady));
            OnPropertyChanged(nameof(AgentStatusText));
            OnPropertyChanged(nameof(AgentDetailText));
            OnPropertyChanged(nameof(DatabaseStatusText));
            OnPropertyChanged(nameof(GitStatusText));
            OnPropertyChanged(nameof(ProjectDetectionText));
            OnPropertyChanged(nameof(IsGodotProject));
            OnPropertyChanged(nameof(GodotStatusText));
            OnPropertyChanged(nameof(GodotDetailText));
        }
    }

    private ProjectWorldInitializationRequest BuildRequest() => new(
        new ProjectRef(_openResult.Project.Id),
        new UserPrincipalRef(UserPrincipal),
        SelectedRoleKind,
        ResponsibilityObligation,
        ResponsibilityExpectedOutcome,
        InitialAssignment);
}
