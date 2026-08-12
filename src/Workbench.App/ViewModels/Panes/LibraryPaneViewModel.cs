using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Project.Opening;
using Workbench.Core.Leaders;
using Workbench.App.Leader;
using Workbench.Storage.Settings;
using Workbench.Storage.Memory;
using Workbench.Storage.Leaders;
using System.Collections.ObjectModel;
using System.Globalization;

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
    private readonly ProjectMemoryService? _memory;
    private readonly ProjectMemorySynthesisRepository? _synthesisJobs;
    private readonly LeaderSessionEpochRepository? _epochRepository;
    private readonly Action<Guid>? _scheduleMemorySynthesis;
    private readonly Dictionary<Guid, string> _candidateSourceLabels = [];

    public LibraryPaneViewModel(
        ProjectOpenResult result,
        Func<Task> focus,
        ProjectSettingsRepository? projectSettings = null,
        LeaderSessionRotationStateService? rotationState = null,
        ProjectMemoryService? memory = null,
        ProjectMemorySynthesisRepository? synthesisJobs = null,
        LeaderSessionEpochRepository? epochRepository = null,
        Action<Guid>? scheduleMemorySynthesis = null)
    {
        Result = result;
        _focus = focus;
        _projectSettings = projectSettings;
        _rotationState = rotationState;
        _memory = memory;
        _synthesisJobs = synthesisJobs;
        _epochRepository = epochRepository;
        _scheduleMemorySynthesis = scheduleMemorySynthesis;
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

    public ObservableCollection<ProjectMemoryItem> PendingCandidates { get; } = [];
    public ObservableCollection<ProjectMemoryItem> FormalMemories { get; } = [];
    public ObservableCollection<ProjectMemoryItem> LearnedMemories { get; } = [];
    public string LearnedMemoryLabel => "Learned Memory · AI-generated";
    [ObservableProperty] public partial string MemoryLearningStatus { get; set; } = "Memory learning: Up to date";
    [ObservableProperty] public partial string? SelectedCandidateSourceLabel { get; set; }
    [ObservableProperty] public partial ProjectMemoryItem? SelectedCandidate { get; set; }
    [ObservableProperty] public partial string CandidateEditContent { get; set; } = string.Empty;
    public int PendingCandidateCount => PendingCandidates.Count;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_rotationState is not null)
        {
            var state = await _rotationState.GetAsync(Result.Project.Id, cancellationToken);
            RotationPolicyOverride = state.ProjectOverride;
            EffectiveRotationPolicy = state.EffectivePolicy;
        }
        if (_memory is not null) await LoadMemoryAsync(cancellationToken);
    }

    public async Task LoadMemoryAsync(CancellationToken cancellationToken=default)
    {
        if (_memory is null) return;
        PendingCandidates.Clear(); foreach(var item in await _memory.GetPendingCandidatesAsync(Result.Project.Id,cancellationToken)) PendingCandidates.Add(item);
        FormalMemories.Clear(); foreach(var item in await _memory.GetFormalMemoriesAsync(Result.Project.Id,cancellationToken)) FormalMemories.Add(item);
        LearnedMemories.Clear(); foreach(var item in await _memory.GetLearnedMemoriesAsync(Result.Project.Id,cancellationToken)) LearnedMemories.Add(item);
        _candidateSourceLabels.Clear();
        if (_epochRepository is not null)
        {
            foreach (var candidate in PendingCandidates)
            {
                var source = (await _memory.GetSourcesAsync(candidate.Id, cancellationToken))
                    .FirstOrDefault(value => value.SourceType == "LeaderEpoch" && Guid.TryParse(value.SourceRef, out _));
                if (source is not null && Guid.TryParse(source.SourceRef, out var epochId))
                {
                    var epoch = await _epochRepository.GetAsync(epochId, cancellationToken);
                    if (epoch?.EndedAt is not null)
                    {
                        _candidateSourceLabels[candidate.Id] = $"From Leader session · {epoch.EndedAt.Value.ToString("MMM d", CultureInfo.InvariantCulture)}";
                    }
                }
            }
        }
        if (_synthesisJobs is not null)
        {
            var status = await _synthesisJobs.GetStatusAsync(Result.Project.Id, cancellationToken);
            MemoryLearningStatus = status.RunningCount > 0
                ? "Learning..."
                : status.PendingCount > 0
                    ? $"Memory learning: {status.PendingCount} session{(status.PendingCount == 1 ? string.Empty : "s")} pending"
                    : "Memory learning: Up to date";
        }
        OnPropertyChanged(nameof(PendingCandidateCount));
    }
    [RelayCommand] private async Task AcceptCandidate(){if(_memory is null||SelectedCandidate is null)return;await _memory.AcceptCandidateAsync(SelectedCandidate.Id);await LoadMemoryAsync();SelectedCandidate=null;}
    [RelayCommand] private async Task EditAndAcceptCandidate(){if(_memory is null||SelectedCandidate is null)return;await _memory.EditAndAcceptCandidateAsync(SelectedCandidate.Id,CandidateEditContent);await LoadMemoryAsync();SelectedCandidate=null;CandidateEditContent=string.Empty;}
    [RelayCommand] private async Task RejectCandidate(){if(_memory is null||SelectedCandidate is null)return;await _memory.RejectCandidateAsync(SelectedCandidate.Id);await LoadMemoryAsync();SelectedCandidate=null;}
    [RelayCommand] private void SelectCandidate(ProjectMemoryItem candidate) { SelectedCandidate = candidate; CandidateEditContent = candidate.Content; SelectedCandidateSourceLabel = _candidateSourceLabels.GetValueOrDefault(candidate.Id); }

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
    private async Task ShowProject()
    {
        SelectedSection = LibrarySection.Project;
        await LoadMemoryAsync();
        _scheduleMemorySynthesis?.Invoke(Result.Project.Id);
    }

    [RelayCommand]
    private Task Focus() => _focus();
}
