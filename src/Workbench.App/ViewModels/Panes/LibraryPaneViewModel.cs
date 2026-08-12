using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Project.Opening;
using Workbench.Core.Leaders;
using Workbench.App.Leader;
using Workbench.Storage.Settings;

namespace Workbench.App.ViewModels.Panes;

public enum LibrarySection
{
    Overview,
    Browse,
    History,
    Project
}

public partial class LibraryPaneViewModel : ViewModelBase
{
    private readonly Func<Task> _focus;
    private readonly ProjectSettingsRepository? _projectSettings;
    private readonly LeaderSessionRotationStateService? _rotationState;

    public LibraryPaneViewModel(
        ProjectOpenResult result,
        Func<Task> focus,
        ProjectSettingsRepository? projectSettings = null,
        LeaderSessionRotationStateService? rotationState = null)
    {
        Result = result;
        _focus = focus;
        _projectSettings = projectSettings;
        _rotationState = rotationState;
    }

    public LibraryPaneViewModel(ProjectOpenResult result)
        : this(result, () => Task.CompletedTask)
    {
    }

    public ProjectOpenResult Result { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverview), nameof(IsBrowse), nameof(IsHistory), nameof(IsProject))]
    public partial LibrarySection SelectedSection { get; set; } = LibrarySection.Overview;

    public bool IsOverview => SelectedSection == LibrarySection.Overview;

    public bool IsBrowse => SelectedSection == LibrarySection.Browse;

    public bool IsHistory => SelectedSection == LibrarySection.History;

    public bool IsProject => SelectedSection == LibrarySection.Project;

    public string ProjectName => Result.Project.Name;

    public string ProjectType => Result.Project.Type.ToString();

    public string ProjectPath => Result.Project.RootPath;

    public string GitStatus => !Result.Git.GitInstalled
        ? "Unavailable"
        : Result.Git.IsRepository ? "Repository" : "No Repository";

    public string BranchText => Result.Git.IsDetachedHead ? "Detached" : Result.Git.BranchName ?? "—";

    public string ShortHead => string.IsNullOrWhiteSpace(Result.Git.HeadCommit)
        ? "—"
        : Result.Git.HeadCommit[..Math.Min(10, Result.Git.HeadCommit.Length)];

    public string WorkingTreeText => Result.Git.IsRepository
        ? Result.Git.IsDirty ? "Modified" : "Clean"
        : "—";

    public string? StatusMessage => Result.Git.Error is null ? null : "Git status unavailable";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotationPolicySummary))]
    public partial LeaderSessionRotationPolicy? RotationPolicyOverride { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotationPolicySummary))]
    public partial LeaderSessionRotationPolicy EffectiveRotationPolicy { get; set; } = LeaderSessionRotationPolicy.Auto;

    public string RotationPolicySummary => RotationPolicyOverride is null
        ? $"Using global setting ({EffectiveRotationPolicy})."
        : $"Project setting: {EffectiveRotationPolicy}.";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_rotationState is null)
        {
            return;
        }

        var state = await _rotationState.GetAsync(Result.Project.Id, cancellationToken);
        RotationPolicyOverride = state.ProjectOverride;
        EffectiveRotationPolicy = state.EffectivePolicy;
    }

    public async Task SetLeaderSessionRotationPolicyOverrideAsync(
        LeaderSessionRotationPolicy? policy,
        CancellationToken cancellationToken = default)
    {
        if (_projectSettings is null)
        {
            throw new InvalidOperationException("Project settings are unavailable.");
        }

        await _projectSettings.SaveLeaderSessionRotationPolicyOverrideAsync(Result.Project.Id, policy, cancellationToken);
        RotationPolicyOverride = policy;
        if (_rotationState is not null)
        {
            EffectiveRotationPolicy = (await _rotationState.GetAsync(Result.Project.Id, cancellationToken)).EffectivePolicy;
        }
    }

    [RelayCommand]
    private Task UseGlobalRotationPolicy() => SetLeaderSessionRotationPolicyOverrideAsync(null);

    [RelayCommand]
    private Task UseAutomaticRotation() => SetLeaderSessionRotationPolicyOverrideAsync(LeaderSessionRotationPolicy.Auto);

    [RelayCommand]
    private Task AskBeforeRotation() => SetLeaderSessionRotationPolicyOverrideAsync(LeaderSessionRotationPolicy.Ask);

    [RelayCommand]
    private Task UseManualRotation() => SetLeaderSessionRotationPolicyOverrideAsync(LeaderSessionRotationPolicy.ManualOnly);

    [RelayCommand]
    private void ShowOverview() => SelectedSection = LibrarySection.Overview;

    [RelayCommand]
    private void ShowBrowse() => SelectedSection = LibrarySection.Browse;

    [RelayCommand]
    private void ShowHistory() => SelectedSection = LibrarySection.History;

    [RelayCommand]
    private void ShowProject() => SelectedSection = LibrarySection.Project;

    [RelayCommand]
    private Task Focus() => _focus();
}
