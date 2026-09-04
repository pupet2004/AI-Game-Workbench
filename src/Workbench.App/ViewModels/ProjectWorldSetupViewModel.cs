using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.ProjectWorld;
using Workbench.App.Services;
using Workbench.Core.Continuity;
using Workbench.Project.Opening;

namespace Workbench.App.ViewModels;

public sealed partial class ProjectWorldSetupViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly ProjectOpenResult _openResult;
    private readonly Func<Task> _backToHome;
    private readonly Func<ProjectOpenResult, Task> _openWorkspace;

    public ProjectWorldSetupViewModel(
        AppServices services,
        ProjectOpenResult openResult,
        ProjectWorldEntryStatus status,
        Func<Task> backToHome,
        Func<ProjectOpenResult, Task> openWorkspace)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _openResult = openResult ?? throw new ArgumentNullException(nameof(openResult));
        Status = status ?? throw new ArgumentNullException(nameof(status));
        _backToHome = backToHome ?? throw new ArgumentNullException(nameof(backToHome));
        _openWorkspace = openWorkspace ?? throw new ArgumentNullException(nameof(openWorkspace));
        UserPrincipal = _services.UserPrincipalProvider.GetCurrent().Value;
        ResponsibilityObligation = LocalizationService.Current["ProjectSetup.DefaultResponsibility"];
        ResponsibilityExpectedOutcome = LocalizationService.Current["ProjectSetup.DefaultExpected"];
        InitialAssignment = LocalizationService.Current["ProjectSetup.DefaultAssignment"];
        IsGovernanceEstablished = status.Kind is ProjectWorldEntryKind.ProjectWorldReady or ProjectWorldEntryKind.ProjectWorldSetupIncomplete;
    }

    public ProjectWorldEntryStatus Status { get; }
    public string ProjectName => _openResult.Project.Name;
    public string ProjectPath => _openResult.Project.RootPath;
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
    [NotifyPropertyChangedFor(nameof(CanPreview))]
    public partial bool IsGovernanceEstablished { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    public partial bool IsPreviewVisible { get; set; }

    [ObservableProperty]
    public partial string? PreviewText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreview), nameof(CanConfirm))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public bool CanPreview => !IsBusy && IsGovernanceEstablished &&
        !string.IsNullOrWhiteSpace(UserPrincipal) &&
        !string.IsNullOrWhiteSpace(ResponsibilityObligation) &&
        !string.IsNullOrWhiteSpace(ResponsibilityExpectedOutcome) &&
        !string.IsNullOrWhiteSpace(InitialAssignment);

    public bool CanConfirm => CanPreview && IsPreviewVisible;

    public bool CanEstablishGovernance => !IsBusy && !IsGovernanceEstablished &&
        Status.Kind is ProjectWorldEntryKind.UnmanagedProjectUnavailable or ProjectWorldEntryKind.LegacySetupRequired;

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
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Project setup could not be completed; nothing was changed. {exception.Message}";
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
            var preview = await _services.ProjectWorldInitialization.PreviewAsync(BuildRequest());
            PreviewText = $"{preview.EffectsSummary}\n\nActing as: {preview.RoleKind}\nResponsibility: {preview.ResponsibilityObligation}\nAssignment: {preview.InitialAssignment}";
            IsPreviewVisible = true;
        }
        catch (Exception exception)
        {
            IsPreviewVisible = false;
            PreviewText = null;
            ErrorMessage = $"Preview could not be created; nothing was changed. {exception.Message}";
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
            await _services.ProjectWorldInitialization.CommitAsync(BuildRequest());
            await _openWorkspace(_openResult);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Project setup could not be completed; nothing was changed. {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task BackAsync() => _backToHome();

    private ProjectWorldInitializationRequest BuildRequest() => new(
        new ProjectRef(_openResult.Project.Id),
        new UserPrincipalRef(UserPrincipal),
        SelectedRoleKind,
        ResponsibilityObligation,
        ResponsibilityExpectedOutcome,
        InitialAssignment);
}
