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
    Category,
    Time,
    Project
}

public enum LibraryTimelineDirection
{
    OldToNew
}

public sealed record LibraryTimelineNodeView(
    Guid Id,
    Guid ObjectId,
    DateOnly LocalDate,
    string Content,
    IReadOnlyList<LibraryMaterialReference> Materials);

public sealed record LibraryTimeGroupView(
    DateOnly LocalDate,
    string Category,
    string Topic,
    Guid ObjectId,
    IReadOnlyList<LibraryTimelineNodeView> Nodes);

public sealed record LibraryCategoryGroupView(
    string Category,
    IReadOnlyList<ProjectLibraryObject> Objects);

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
    private readonly ProjectLibraryRepository? _library;
    private readonly ProjectLibraryEvolutionRepository? _evolutionLibrary;

    public LibraryPaneViewModel(
        ProjectOpenResult result,
        Func<Task> focus,
        ProjectSettingsRepository? projectSettings = null,
        LeaderSessionRotationStateService? rotationState = null,
        ProjectMemoryService? memory = null,
        ProjectMemorySynthesisRepository? synthesisJobs = null,
        LeaderSessionEpochRepository? epochRepository = null,
        Action<Guid>? scheduleMemorySynthesis = null,
        ProjectLibraryRepository? library = null,
        ProjectLibraryEvolutionRepository? evolutionLibrary = null)
    {
        Result = result;
        _focus = focus;
        _projectSettings = projectSettings;
        _rotationState = rotationState;
        _memory = memory;
        _synthesisJobs = synthesisJobs;
        _epochRepository = epochRepository;
        _scheduleMemorySynthesis = scheduleMemorySynthesis;
        _library = library;
        _evolutionLibrary = evolutionLibrary;
    }

    public LibraryPaneViewModel(ProjectOpenResult result)
        : this(result, () => Task.CompletedTask)
    {
    }

    public ProjectOpenResult Result { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCategory), nameof(IsTime), nameof(HasCategoryView), nameof(HasTimeView), nameof(IsProject))]
    public partial LibrarySection SelectedSection { get; set; } = LibrarySection.Category;

    public bool IsCategory => SelectedSection == LibrarySection.Category;

    public bool HasCategoryView => IsCategory;

    public bool IsTime => SelectedSection == LibrarySection.Time;

    public bool HasTimeView => IsTime;

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
    public ObservableCollection<ProjectLibraryEntry> LibraryEntries { get; } = [];
    [ObservableProperty] public partial string? LibraryCategoryFilter { get; set; }
    [ObservableProperty] public partial string? LibraryTopicFilter { get; set; }
    [ObservableProperty] public partial string? LibraryTextFilter { get; set; }
    public bool HasLibraryEntries => LibraryEntries.Count > 0;

    public ObservableCollection<string> LibraryCategories { get; } = [];
    public ObservableCollection<LibraryCategoryGroupView> CategoryGroups { get; } = [];
    public ObservableCollection<ProjectLibraryObject> LibraryObjects { get; } = [];
    public ObservableCollection<LibraryTimelineNodeView> ObjectTimeline { get; } = [];
    public ObservableCollection<DateOnly> TimeDates { get; } = [];
    public ObservableCollection<LibraryTimeGroupView> TimeGroups { get; } = [];
    public LibraryTimelineDirection TimelineDirection => LibraryTimelineDirection.OldToNew;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentOverviewText))]
    public partial ProjectLibraryObject? SelectedLibraryObject { get; set; }

    public string CurrentOverviewText => string.IsNullOrWhiteSpace(SelectedLibraryObject?.CurrentOverview)
        ? "No current overview yet."
        : SelectedLibraryObject.CurrentOverview;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_rotationState is not null)
        {
            var state = await _rotationState.GetAsync(Result.Project.Id, cancellationToken);
            RotationPolicyOverride = state.ProjectOverride;
            EffectiveRotationPolicy = state.EffectivePolicy;
        }
        await LoadLibraryAsync(cancellationToken);
    }

    public async Task LoadLibraryAsync(CancellationToken cancellationToken = default)
    {
        if (_evolutionLibrary is not null)
        {
            await LoadEvolutionLibraryAsync(cancellationToken);
            return;
        }

        if (_library is null) return;
        LibraryEntries.Clear();
        foreach (var entry in await _library.BrowseAsync(Result.Project.Id, LibraryCategoryFilter, LibraryTopicFilter, LibraryTextFilter, cancellationToken)) LibraryEntries.Add(entry);
        OnPropertyChanged(nameof(HasLibraryEntries));
    }

    [RelayCommand]
    private Task ApplyLibraryFilter() => LoadLibraryAsync();

    public async Task ShowCategoryAsync(CancellationToken cancellationToken = default)
    {
        SelectedSection = LibrarySection.Category;
        await LoadLibraryAsync(cancellationToken);
    }

    public async Task ShowTimeAsync(CancellationToken cancellationToken = default)
    {
        SelectedSection = LibrarySection.Time;
        await LoadLibraryAsync(cancellationToken);
    }

    public async Task SelectLibraryObjectAsync(Guid objectId, CancellationToken cancellationToken = default)
    {
        if (_evolutionLibrary is null) return;
        var selected = await _evolutionLibrary.GetObjectAsync(Result.Project.Id, objectId, cancellationToken);
        if (selected is null) return;

        SelectedLibraryObject = selected;
        ObjectTimeline.Clear();
        foreach (var node in await _evolutionLibrary.GetTimelineAsync(Result.Project.Id, objectId, cancellationToken))
        {
            ObjectTimeline.Add(await CreateTimelineNodeViewAsync(node, cancellationToken));
        }
    }

    [RelayCommand]
    private Task SelectLibraryObject(Guid objectId) => SelectLibraryObjectAsync(objectId);

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
    private Task ShowCategory() => ShowCategoryAsync();

    [RelayCommand]
    private Task ShowTime() => ShowTimeAsync();

    [RelayCommand]
    private void ShowProject() => SelectedSection = LibrarySection.Project;

    [RelayCommand]
    private Task Focus() => _focus();

    private async Task LoadEvolutionLibraryAsync(CancellationToken cancellationToken)
    {
        var nodes = await _evolutionLibrary!.BrowseNodesByDateAsync(Result.Project.Id, cancellationToken: cancellationToken);
        var objects = (await _evolutionLibrary.ListObjectsAsync(Result.Project.Id, cancellationToken))
            .ToDictionary(value => value.Id);

        LibraryCategories.Clear();
        foreach (var category in objects.Values.Select(value => value.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase)) LibraryCategories.Add(category);
        LibraryObjects.Clear();
        foreach (var libraryObject in objects.Values.OrderBy(value => value.CategoryKey).ThenBy(value => value.TopicKey)) LibraryObjects.Add(libraryObject);
        CategoryGroups.Clear();
        foreach (var group in LibraryObjects.GroupBy(value => value.Category, StringComparer.OrdinalIgnoreCase))
        {
            CategoryGroups.Add(new(group.Key, group.ToArray()));
        }

        TimeDates.Clear();
        foreach (var date in nodes.Select(node => node.LocalDate).Distinct()) TimeDates.Add(date);
        TimeGroups.Clear();
        foreach (var group in nodes.GroupBy(node => (node.LocalDate, node.ObjectId)))
        {
            if (!objects.TryGetValue(group.Key.ObjectId, out var libraryObject)) continue;
            var views = new List<LibraryTimelineNodeView>();
            foreach (var node in group) views.Add(await CreateTimelineNodeViewAsync(node, cancellationToken));
            TimeGroups.Add(new(group.Key.LocalDate, libraryObject.Category, libraryObject.Topic, libraryObject.Id, views));
        }
    }

    private async Task<LibraryTimelineNodeView> CreateTimelineNodeViewAsync(ProjectLibraryTimelineNode node, CancellationToken cancellationToken) =>
        new(node.Id, node.ObjectId, node.LocalDate, node.Content,
            await _evolutionLibrary!.GetMaterialReferencesAsync(Result.Project.Id, node.Id, cancellationToken));
}
