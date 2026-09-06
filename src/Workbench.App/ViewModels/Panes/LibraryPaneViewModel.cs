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
using Workbench.App.Memory;
using Workbench.App.Services;

namespace Workbench.App.ViewModels.Panes;

public enum LibrarySection
{
    Overview,
    Category,
    Time,
    Project
}
public enum LibraryTimelineDirection
{
    OldToNew
}
public enum LibraryTimelineContextKind
{
    LibraryRecord,
    B1AcceptedProjection,
    LegacyContext
}
public sealed record LibraryTimelineNodeView(
    Guid Id,
    Guid ObjectId,
    DateOnly LocalDate,
    DateTimeOffset OccurredAt,
    string Content,
    IReadOnlyList<LibraryMaterialReference> Materials,
    LibraryTimelineContextKind ContextKind)
{
    public string SourceLabel => LocalizationService.Current["Library.Source"];
    public string TimestampText => OccurredAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
    public bool IsLegacyContext => ContextKind == LibraryTimelineContextKind.LegacyContext;

    public string ContextLabel => ContextKind switch
    {
        // These context labels are protocol/debug identifiers and remain stable in logs and reviews.
        LibraryTimelineContextKind.B1AcceptedProjection => "B1 ACCEPTED PROJECTION",
        LibraryTimelineContextKind.LegacyContext => "LEGACY CONTEXT",
        _ => "LIBRARY RECORD"
    };
}

public sealed record LibraryTimeGroupView(
    DateOnly LocalDate,
    string Category,
    string Topic,
    Guid ObjectId,
    IReadOnlyList<LibraryTimelineNodeView> Nodes);

public sealed record LibrarySummaryEntryView(
    DateTimeOffset OccurredAt,
    SummaryDeltaKind Kind,
    string Text,
    IReadOnlyList<SummarySourceRef> SourceRefs)
{
    public string KindLabel => Kind switch
    {
        SummaryDeltaKind.Decision => LocalizationService.Current["Library.Decision"],
        SummaryDeltaKind.Change => LocalizationService.Current["Library.Change"],
        SummaryDeltaKind.Constraint => LocalizationService.Current["Library.Constraint"],
        SummaryDeltaKind.RejectedPath => LocalizationService.Current["Library.RejectedPath"],
        _ => LocalizationService.Current["Library.Unresolved"]
    };

    public string TimestampText => OccurredAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
}

public sealed record LibraryTimeEventView(
    DateTimeOffset OccurredAt,
    string Category,
    string Topic,
    string Text,
    IReadOnlyList<LibraryMaterialReference> Materials,
    IReadOnlyList<SummarySourceRef> SummarySources)
{
    public string SourceLabel => LocalizationService.Current["Library.Source"];
    public string TimestampText => OccurredAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
}

public sealed record LibraryTimeDayView(
    DateOnly LocalDate,
    DailySummaryDocument? Summary,
    IReadOnlyList<LibrarySummaryEntryView> SummaryEntries,
    IReadOnlyList<LibraryTimeGroupView> Groups);

public sealed record LibraryCategoryGroupView(
    string Category,
    IReadOnlyList<ProjectLibraryObject> Objects);

public sealed record LibrarySearchResultView(
    string Kind,
    string AuthorityStatus,
    string Category,
    string Topic,
    string Text,
    string Source);

internal sealed record LibraryProjectionSearchEntry(
    Guid ObjectId,
    string Text,
    string Source);

public partial class LibraryPaneViewModel : ViewModelBase
{
    private readonly Func<Task> _focus;
    private readonly ProjectSettingsRepository? _projectSettings;
    private readonly LeaderSessionRotationStateService? _rotationState;
    private readonly ProjectMemoryService? _memory;
    private readonly ProjectMemorySynthesisRepository? _synthesisJobs;
    private readonly LeaderSessionEpochRepository? _epochRepository;
    private readonly Dictionary<Guid, string> _candidateSourceLabels = [];
    private readonly ProjectLibraryRepository? _library;
    private readonly ProjectLibraryEvolutionRepository? _evolutionLibrary;
    private readonly IProjectMemoryApi? _projectMemoryApi;
    private readonly ProjectSummaryRepository? _projectSummaryRepository;
    private readonly LibraryAcceptedStateReader? _acceptedStateReader;
    private IReadOnlyList<LibrarySummaryEntryView> _summarySearchEntries = [];
    private IReadOnlyList<LibraryProjectionSearchEntry> _libraryProjectionSearchEntries = [];

    public LibraryPaneViewModel(
        ProjectOpenResult result,
        Func<Task> focus,
        ProjectSettingsRepository? projectSettings = null,
        LeaderSessionRotationStateService? rotationState = null,
        ProjectMemoryService? memory = null,
        ProjectMemorySynthesisRepository? synthesisJobs = null,
        LeaderSessionEpochRepository? epochRepository = null,
        ProjectLibraryRepository? library = null,
        ProjectLibraryEvolutionRepository? evolutionLibrary = null,
        IProjectMemoryApi? projectMemoryApi = null,
        ProjectSummaryRepository? projectSummaryRepository = null,
        LibraryAcceptedStateReader? acceptedStateReader = null)
    {
        Result = result;
        _focus = focus;
        _projectSettings = projectSettings;
        _rotationState = rotationState;
        _memory = memory;
        _synthesisJobs = synthesisJobs;
        _epochRepository = epochRepository;
        _library = library;
        _evolutionLibrary = evolutionLibrary;
        _projectMemoryApi = projectMemoryApi;
        _projectSummaryRepository = projectSummaryRepository;
        _acceptedStateReader = acceptedStateReader;
    }

    public LibraryPaneViewModel(ProjectOpenResult result)
        : this(result, () => Task.CompletedTask)
    {
    }

    public ProjectOpenResult Result { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverview), nameof(HasOverviewView), nameof(IsCategory), nameof(IsTime), nameof(HasCategoryView), nameof(HasTimeView), nameof(IsProject))]
    public partial LibrarySection SelectedSection { get; set; } = LibrarySection.Overview;

    public bool IsOverview => SelectedSection == LibrarySection.Overview;

    public bool HasOverviewView => IsOverview;

    public bool IsCategory => SelectedSection == LibrarySection.Category;

    public bool HasCategoryView => IsCategory;

    public bool IsTime => SelectedSection == LibrarySection.Time;

    public bool HasTimeView => IsTime;

    public bool IsProject => SelectedSection == LibrarySection.Project;

    public bool HasB1ProjectWorld => AcceptedStateReadModel is not null;

    public string AcceptedStateStatus => AcceptedStateReadModel is null
        ? LocalizationService.Current["Dynamic.AcceptedProjectionUnavailable"]
        : AcceptedStateReadModel.CurrentContributions.Count == 0
            ? LocalizationService.Current["Explorer.NoAccepted"]
            : string.Format(LocalizationService.Current["Dynamic.AcceptedStatementsCount"], AcceptedStateReadModel.CurrentContributions.Count);

    public string ProjectName => Result.Project.Name;

    public string ProjectType => Result.Project.Type.ToString();

    public string ProjectPath => Result.Project.RootPath;

    public string GitStatus => !Result.Git.GitInstalled
        ? LocalizationService.Current["Dynamic.Unavailable"]
        : Result.Git.IsRepository ? LocalizationService.Current["Dynamic.Repository"] : LocalizationService.Current["Dynamic.NoRepository"];

    public string BranchText => Result.Git.IsDetachedHead ? LocalizationService.Current["Dynamic.Detached"] : Result.Git.BranchName ?? "—";

    public string ShortHead => string.IsNullOrWhiteSpace(Result.Git.HeadCommit)
        ? "—"
        : Result.Git.HeadCommit[..Math.Min(10, Result.Git.HeadCommit.Length)];

    public string WorkingTreeText => Result.Git.IsRepository
        ? Result.Git.IsDirty ? LocalizationService.Current["Dynamic.Modified"] : LocalizationService.Current["Dynamic.Clean"]
        : "—";

    public string? StatusMessage => Result.Git.Error is null ? null : LocalizationService.Current["Dynamic.GitUnavailable"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotationPolicySummary))]
    public partial LeaderSessionRotationPolicy? RotationPolicyOverride { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotationPolicySummary))]
    public partial LeaderSessionRotationPolicy EffectiveRotationPolicy { get; set; } = LeaderSessionRotationPolicy.Auto;

    public string RotationPolicySummary => RotationPolicyOverride is null
        ? string.Format(LocalizationService.Current["Dynamic.RotationUsingGlobal"], EffectiveRotationPolicy)
        : string.Format(LocalizationService.Current["Dynamic.RotationProject"], EffectiveRotationPolicy);

    public ObservableCollection<ProjectMemoryItem> PendingCandidates { get; } = [];
    public ObservableCollection<ProjectMemoryItem> FormalMemories { get; } = [];
    public ObservableCollection<ProjectMemoryItem> LearnedMemories { get; } = [];
    public string LearnedMemoryLabel => LocalizationService.Current["Dynamic.LearnedMemory"];
    [ObservableProperty] public partial string MemoryLearningStatus { get; set; } = LocalizationService.Current["Dynamic.MemoryUpToDate"];
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
    public ObservableCollection<LibrarySearchResultView> SearchResults { get; } = [];
    public bool HasSearchResults => SearchResults.Count > 0;
    [ObservableProperty] public partial string? SelectedCategory { get; set; }
    public ObservableCollection<ProjectLibraryObject> SelectedCategoryObjects { get; } = [];
    public ObservableCollection<LibraryTimelineNodeView> ObjectTimeline { get; } = [];
    public ObservableCollection<DateOnly> TimeDates { get; } = [];
    public ObservableCollection<LibraryTimeGroupView> TimeGroups { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelectedTimeDay))] public partial LibraryTimeDayView? SelectedTimeDay { get; set; }
    public bool HasSelectedTimeDay => SelectedTimeDay is not null;
    public ObservableCollection<LibraryTimeDayView> TimeDays { get; } = [];
    public ObservableCollection<int> TimeYears { get; } = [];
    public ObservableCollection<int> TimeMonths { get; } = [];
    public ObservableCollection<DateOnly> TimeDaysInMonth { get; } = [];
    public ObservableCollection<LibraryTimeEventView> TimeEvents { get; } = [];
    private readonly Dictionary<DateOnly, IReadOnlyList<LibraryTimeEventView>> _timeEventsByDate = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelectedTimeYear))] public partial int? SelectedTimeYear { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelectedTimeMonth))] public partial int? SelectedTimeMonth { get; set; }
    public bool HasSelectedTimeYear => SelectedTimeYear is not null;
    public bool HasSelectedTimeMonth => SelectedTimeMonth is not null;
    public LibraryTimelineDirection TimelineDirection => LibraryTimelineDirection.OldToNew;

    public ObservableCollection<ProjectLibraryProposal> PendingLibraryProposals { get; } = [];
    public bool HasPendingLibraryProposals => PendingLibraryProposals.Count > 0;

    public ObservableCollection<LibraryAcceptedContributionProjection> CurrentAcceptedContributions { get; } = [];
    public ObservableCollection<LibraryProjectDecisionProjection> ProjectLevelDecisions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasB1ProjectWorld), nameof(AcceptedStateStatus))]
    public partial LibraryAcceptedStateReadModel? AcceptedStateReadModel { get; set; }

    [ObservableProperty]
    public partial ProjectLibraryProposal? SelectedLibraryProposal { get; set; }

    [ObservableProperty]
    public partial string ProposalEditContent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ProposalEditOverview { get; set; }

    [ObservableProperty]
    public partial string? LibraryProposalStatusMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentOverviewText))]
    public partial ProjectLibraryObject? SelectedLibraryObject { get; set; }

    public string CurrentOverviewText => string.IsNullOrWhiteSpace(SelectedLibraryObject?.CurrentOverview)
        ? LocalizationService.Current["Dynamic.NoOverview"]
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

    [RelayCommand]
    private Task SearchLibraryAsync()
    {
        SearchResults.Clear();
        var query = LibraryTextFilter?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            OnPropertyChanged(nameof(HasSearchResults));
            return Task.CompletedTask;
        }

        bool Match(string value) => value.Contains(query, StringComparison.OrdinalIgnoreCase);

        var libraryResults = new Dictionary<Guid, LibrarySearchResultView>();
        foreach (var item in LibraryObjects)
        {
            if (Match(item.Topic))
            {
                libraryResults[item.Id] = new(
                    "Library projection",
                    "Projection; inspect source",
                    item.Category,
                    item.Topic,
                    item.Topic,
                    "Library object topic");
            }
            else if (Match(item.CurrentOverview ?? string.Empty))
            {
                libraryResults[item.Id] = new(
                    "Library projection",
                    "Projection; inspect source",
                    item.Category,
                    item.Topic,
                    item.CurrentOverview ?? string.Empty,
                    "Library object current overview");
            }
        }

        foreach (var entry in _libraryProjectionSearchEntries.Where(value => Match(value.Text)))
        {
            if (libraryResults.ContainsKey(entry.ObjectId)) continue;
            var item = LibraryObjects.FirstOrDefault(value => value.Id == entry.ObjectId);
            if (item is null) continue;
            libraryResults[item.Id] = new(
                "Library projection",
                "Projection; inspect source",
                item.Category,
                item.Topic,
                entry.Text,
                entry.Source);
        }

        foreach (var item in libraryResults.Values)
        {
            SearchResults.Add(item);
        }

        foreach (var item in CurrentAcceptedContributions.Where(value => Match(value.Contribution.Statement)))
        {
            SearchResults.Add(new("Accepted Fact", "Accepted Project State", "Project", "Accepted contribution", item.Contribution.Statement, item.Contribution.AuthorityDecisionRef.ToString()));
        }

        foreach (var item in PendingLibraryProposals.Where(value => Match(value.Draft.Topic) || Match(value.Draft.NodeContent) || Match(value.Draft.CurrentOverview ?? string.Empty)))
        {
            SearchResults.Add(new("Proposal", "Pending review", item.Draft.Category, item.Draft.Topic, item.Draft.NodeContent, item.Id.ToString()));
        }

        foreach (var item in _summarySearchEntries.Where(value => Match(value.Text)))
        {
            SearchResults.Add(new("Summary", "Summary; not Accepted State", "Summary", item.KindLabel, item.Text, string.Join(", ", item.SourceRefs.Select(source => source.SourceLocator))));
        }

        OnPropertyChanged(nameof(HasSearchResults));
        return Task.CompletedTask;
    }

    public async Task ShowCategoryAsync(CancellationToken cancellationToken = default)
    {
        SelectedSection = LibrarySection.Category;
        SelectedCategory = null;
        SelectedLibraryObject = null;
        ObjectTimeline.Clear();
        await LoadLibraryAsync(cancellationToken);
    }

    public async Task ShowOverviewAsync(CancellationToken cancellationToken = default)
    {
        if (AcceptedStateReadModel is null && _acceptedStateReader is not null)
            await LoadAcceptedStateAsync(cancellationToken);
        SelectedSection = LibrarySection.Overview;
    }

    public async Task ShowTimeAsync(CancellationToken cancellationToken = default)
    {
        SelectedSection = LibrarySection.Time;
        SelectedTimeYear = null;
        SelectedTimeMonth = null;
        SelectedTimeDay = null;
        TimeMonths.Clear();
        TimeDaysInMonth.Clear();
        TimeEvents.Clear();
        await LoadLibraryAsync(cancellationToken);
    }

    public async Task SelectLibraryObjectAsync(Guid objectId, CancellationToken cancellationToken = default)
    {
        if (_evolutionLibrary is null) return;
        if (SelectedLibraryObject?.Id == objectId)
        {
            SelectedLibraryObject = null;
            ObjectTimeline.Clear();
            return;
        }

        var selected = await _evolutionLibrary.GetObjectAsync(Result.Project.Id, objectId, cancellationToken);
        if (selected is null) return;

        SelectedLibraryObject = selected;
        ObjectTimeline.Clear();
        foreach (var node in await _evolutionLibrary.GetTimelineAsync(Result.Project.Id, objectId, cancellationToken))
        {
            ObjectTimeline.Add(await CreateTimelineNodeViewAsync(node, cancellationToken));
        }
    }

    public Task SelectCategoryAsync(string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        if (string.Equals(SelectedCategory, category, StringComparison.OrdinalIgnoreCase))
        {
            SelectedCategory = null;
            SelectedCategoryObjects.Clear();
            return Task.CompletedTask;
        }

        SelectedCategory = category;
        SelectedCategoryObjects.Clear();
        var group = CategoryGroups.FirstOrDefault(value =>
            string.Equals(value.Category, category, StringComparison.OrdinalIgnoreCase));
        if (group is not null)
        {
            foreach (var item in group.Objects)
                SelectedCategoryObjects.Add(item);
        }

        SelectedLibraryObject = null;
        ObjectTimeline.Clear();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task SelectCategory(string category) => SelectCategoryAsync(category);

    public Task SelectTimeDateAsync(DateOnly localDate)
    {
        if (SelectedTimeDay?.LocalDate == localDate)
        {
            SelectedTimeDay = null;
            TimeEvents.Clear();
            return Task.CompletedTask;
        }

        return SelectTimeDayAsync(localDate);
    }

    public Task SelectTimeYearAsync(int year)
    {
        if (SelectedTimeYear == year)
        {
            SelectedTimeYear = null;
            SelectedTimeMonth = null;
            TimeMonths.Clear();
            TimeDaysInMonth.Clear();
            TimeEvents.Clear();
            return Task.CompletedTask;
        }

        SelectedTimeYear = year;
        SelectedTimeMonth = null;
        TimeMonths.Clear();
        foreach (var month in TimeDates.Where(value => value.Year == year).Select(value => value.Month).Distinct().OrderByDescending(value => value))
            TimeMonths.Add(month);
        TimeDaysInMonth.Clear();
        TimeEvents.Clear();
        return Task.CompletedTask;
    }

    public Task SelectTimeMonthAsync(int month)
    {
        if (SelectedTimeYear is null) return Task.CompletedTask;
        if (SelectedTimeMonth == month)
        {
            SelectedTimeMonth = null;
            TimeDaysInMonth.Clear();
            TimeEvents.Clear();
            return Task.CompletedTask;
        }

        SelectedTimeMonth = month;
        TimeDaysInMonth.Clear();
        foreach (var day in TimeDates.Where(value => value.Year == SelectedTimeYear && value.Month == month).OrderByDescending(value => value))
            TimeDaysInMonth.Add(day);
        TimeEvents.Clear();
        return Task.CompletedTask;
    }

    public Task SelectTimeDayAsync(DateOnly localDate)
    {
        SelectedTimeDay = TimeDays.FirstOrDefault(value => value.LocalDate == localDate);
        TimeEvents.Clear();
        if (_timeEventsByDate.TryGetValue(localDate, out var events))
            foreach (var item in events.OrderBy(value => value.OccurredAt)) TimeEvents.Add(item);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task SelectTimeDate(DateOnly localDate) => SelectTimeDateAsync(localDate);

    [RelayCommand]
    private Task SelectTimeYear(int year) => SelectTimeYearAsync(year);

    [RelayCommand]
    private Task SelectTimeMonth(int month) => SelectTimeMonthAsync(month);

    [RelayCommand]
    private Task SelectTimeDay(DateOnly localDate) => SelectTimeDayAsync(localDate);

    [RelayCommand]
    private Task SelectLibraryObject(Guid objectId) => SelectLibraryObjectAsync(objectId);

    partial void OnSelectedLibraryProposalChanged(ProjectLibraryProposal? value)
    {
        ProposalEditContent = value?.Draft.NodeContent ?? string.Empty;
        ProposalEditOverview = value?.Draft.CurrentOverview;
        LibraryProposalStatusMessage = null;
    }

    public async Task AcceptLibraryProposalAsync(CancellationToken cancellationToken = default)
    {
        var proposal = ResolveLibraryProposal();
        if (proposal is null) return;
        await ConfirmLibraryProposalAsync(
            token => _projectMemoryApi!.AcceptLibraryProposalAsync(Result.Project.Id, proposal.Id, token),
            cancellationToken);
    }

    public async Task EditAndAcceptLibraryProposalAsync(CancellationToken cancellationToken = default)
    {
        var proposal = ResolveLibraryProposal();
        if (proposal is null) return;
        var edit = new LibraryProposalEdit(ProposalEditContent, ProposalEditOverview, proposal.Draft.Materials);
        await ConfirmLibraryProposalAsync(
            token => _projectMemoryApi!.EditAndAcceptLibraryProposalAsync(Result.Project.Id, proposal.Id, edit, token),
            cancellationToken);
    }

    public async Task RejectLibraryProposalAsync(CancellationToken cancellationToken = default)
    {
        var proposal = ResolveLibraryProposal();
        if (proposal is null) return;
        if (_projectMemoryApi is null)
        {
            LibraryProposalStatusMessage = LocalizationService.Current["Dynamic.LibraryProposalReviewUnavailable"];
            return;
        }
        await _projectMemoryApi.RejectLibraryProposalAsync(Result.Project.Id, proposal.Id, cancellationToken);
        SelectedLibraryProposal = null;
        LibraryProposalStatusMessage = LocalizationService.Current["Dynamic.LibraryProposalRejected"];
        await LoadLibraryAsync(cancellationToken);
    }

    [RelayCommand]
    private Task AcceptLibraryProposal() => AcceptLibraryProposalAsync();

    [RelayCommand]
    private Task EditAndAcceptLibraryProposal() => EditAndAcceptLibraryProposalAsync();

    [RelayCommand]
    private Task RejectLibraryProposal() => RejectLibraryProposalAsync();

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
                        _candidateSourceLabels[candidate.Id] = $"{LocalizationService.Current["Dynamic.FromLeaderSession"]} · {epoch.EndedAt.Value.ToString("MMM d", CultureInfo.InvariantCulture)}";
                    }
                }
            }
        }
        if (_synthesisJobs is not null)
        {
            var status = await _synthesisJobs.GetStatusAsync(Result.Project.Id, cancellationToken);
            MemoryLearningStatus = status.RunningCount > 0
                ? LocalizationService.Current["Dynamic.Learning"]
                : status.PendingCount > 0
                    ? string.Format(LocalizationService.Current["Dynamic.MemorySessionsPending"], status.PendingCount)
                    : LocalizationService.Current["Dynamic.MemoryUpToDate"];
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
    private Task ShowOverview() => ShowOverviewAsync();

    [RelayCommand]
    private Task ShowTime() => ShowTimeAsync();

    [RelayCommand]
    private void ShowProject() => SelectedSection = LibrarySection.Project;

    [RelayCommand]
    private Task Focus() => _focus();

    private async Task LoadEvolutionLibraryAsync(CancellationToken cancellationToken)
    {
        await LoadAcceptedStateAsync(cancellationToken);
        var nodes = await _evolutionLibrary!.BrowseNodesByDateAsync(Result.Project.Id, cancellationToken: cancellationToken);
        var objects = (await _evolutionLibrary.ListObjectsAsync(Result.Project.Id, cancellationToken))
            .ToDictionary(value => value.Id);
        var dailySummaryMetadata = _projectMemoryApi is null
            ? []
            : await _projectMemoryApi.ListDailySummaryMetadataAsync(Result.Project.Id, cancellationToken: cancellationToken);
        var summaryEntries = _projectSummaryRepository is null
            ? []
            : await _projectSummaryRepository.QueryAsync(new SummaryQuery(Result.Project.Id, 200), cancellationToken);
        _summarySearchEntries = summaryEntries
            .Select(entry => new LibrarySummaryEntryView(entry.OccurredAt, entry.Kind, entry.Text, entry.SourceRefs))
            .ToArray();

        var projectionSearchEntries = new List<LibraryProjectionSearchEntry>();
        foreach (var node in nodes)
        {
            if (!objects.ContainsKey(node.ObjectId)) continue;
            projectionSearchEntries.Add(new(node.ObjectId, node.Content, $"Timeline node {node.Id} content"));
            foreach (var material in await _evolutionLibrary.GetMaterialReferencesAsync(Result.Project.Id, node.Id, cancellationToken))
            {
                projectionSearchEntries.Add(new(
                    node.ObjectId,
                    material.Reference,
                    $"Material {material.MaterialKind} reference {material.Reference}"));
                if (!string.IsNullOrWhiteSpace(material.Label))
                {
                    projectionSearchEntries.Add(new(
                        node.ObjectId,
                        material.Label,
                        $"Material {material.MaterialKind} label {material.Label}"));
                }
            }
        }
        _libraryProjectionSearchEntries = projectionSearchEntries;

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
        foreach (var date in nodes.Select(node => node.LocalDate)
                     .Concat(dailySummaryMetadata.Select(summary => summary.LocalDate))
                     .Concat(summaryEntries.Select(entry => DateOnly.FromDateTime(entry.OccurredAt.DateTime)))
                     .Distinct()
                     .OrderByDescending(value => value))
        {
            TimeDates.Add(date);
        }
        TimeGroups.Clear();
        foreach (var group in nodes.GroupBy(node => (node.LocalDate, node.ObjectId)))
        {
            if (!objects.TryGetValue(group.Key.ObjectId, out var libraryObject)) continue;
            var views = new List<LibraryTimelineNodeView>();
            foreach (var node in group) views.Add(await CreateTimelineNodeViewAsync(node, cancellationToken));
            TimeGroups.Add(new(group.Key.LocalDate, libraryObject.Category, libraryObject.Topic, libraryObject.Id, views));
        }

        TimeDays.Clear();
        TimeYears.Clear();
        TimeMonths.Clear();
        TimeDaysInMonth.Clear();
        TimeEvents.Clear();
        _timeEventsByDate.Clear();
        foreach (var year in TimeDates.Select(value => value.Year).Distinct().OrderByDescending(value => value))
            TimeYears.Add(year);
        foreach (var date in TimeDates)
        {
            var summary = _projectMemoryApi is null
                ? null
                : await _projectMemoryApi.GetDailySummaryAsync(Result.Project.Id, date, cancellationToken);
            var entries = summaryEntries
                .Where(entry => DateOnly.FromDateTime(entry.OccurredAt.DateTime) == date)
                .OrderBy(entry => entry.OccurredAt)
                .Select(entry => new LibrarySummaryEntryView(entry.OccurredAt, entry.Kind, entry.Text, entry.SourceRefs))
                .ToArray();
            var groups = TimeGroups.Where(group => group.LocalDate == date).ToArray();
            TimeDays.Add(new(date, summary, entries, groups));

            var events = new List<LibraryTimeEventView>();
            foreach (var group in groups)
            {
                foreach (var node in group.Nodes)
                {
                    events.Add(new(node.OccurredAt, group.Category, group.Topic, node.Content, node.Materials, []));
                }
            }
            events.AddRange(entries.Select(entry => new LibraryTimeEventView(entry.OccurredAt, "Summary", entry.KindLabel, entry.Text, [], entry.SourceRefs)));
            _timeEventsByDate[date] = events.OrderBy(value => value.OccurredAt).ToArray();
        }

        PendingLibraryProposals.Clear();
        if (_projectMemoryApi is not null)
        {
            foreach (var proposal in await _projectMemoryApi.GetPendingLibraryProposalsAsync(Result.Project.Id, cancellationToken))
                PendingLibraryProposals.Add(proposal);
        }
        if (SelectedLibraryProposal is null && PendingLibraryProposals.Count == 1)
            SelectedLibraryProposal = PendingLibraryProposals[0];
        OnPropertyChanged(nameof(HasPendingLibraryProposals));
    }

    private ProjectLibraryProposal? ResolveLibraryProposal()
    {
        var proposal = SelectedLibraryProposal;
        if (proposal is null && PendingLibraryProposals.Count == 1)
        {
            proposal = PendingLibraryProposals[0];
            SelectedLibraryProposal = proposal;
        }

        if (proposal is null)
            LibraryProposalStatusMessage = LocalizationService.Current["Dynamic.LibraryProposalSelectionRequired"];

        return proposal;
    }

    private async Task LoadAcceptedStateAsync(CancellationToken cancellationToken)
    {
        if (_acceptedStateReader is null) return;
        try
        {
            AcceptedStateReadModel = await _acceptedStateReader.ReadAsync(
                new Workbench.Core.Continuity.ProjectRef(Result.Project.Id),
                cancellationToken);
            CurrentAcceptedContributions.Clear();
            foreach (var contribution in AcceptedStateReadModel.CurrentContributions)
                CurrentAcceptedContributions.Add(contribution);
            ProjectLevelDecisions.Clear();
            foreach (var decision in AcceptedStateReadModel.ProjectLevelDecisions)
                ProjectLevelDecisions.Add(decision);
            SelectedSection = LibrarySection.Overview;
        }
        catch (InvalidDataException)
        {
            AcceptedStateReadModel = null;
            CurrentAcceptedContributions.Clear();
            ProjectLevelDecisions.Clear();
            SelectedSection = LibrarySection.Category;
        }
    }

    private async Task ConfirmLibraryProposalAsync(
        Func<CancellationToken, Task> confirm,
        CancellationToken cancellationToken)
    {
        if (_projectMemoryApi is null)
        {
            LibraryProposalStatusMessage = LocalizationService.Current["Dynamic.LibraryProposalReviewUnavailable"];
            return;
        }
        try
        {
            await confirm(cancellationToken);
            SelectedLibraryProposal = null;
            LibraryProposalStatusMessage = LocalizationService.Current["Dynamic.LibraryProposalAccepted"];
            await LoadLibraryAsync(cancellationToken);
        }
        catch (LibraryRevisionConflictException)
        {
            LibraryProposalStatusMessage = LocalizationService.Current["Dynamic.LibraryConflict"];
            await LoadLibraryAsync(cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            LibraryProposalStatusMessage = exception.Message;
            await LoadLibraryAsync(cancellationToken);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            LibraryProposalStatusMessage = LocalizationService.Current["Dynamic.LibraryCommitFailed"];
            await LoadLibraryAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            LibraryProposalStatusMessage = exception.Message;
            await LoadLibraryAsync(cancellationToken);
        }
    }

    private async Task<LibraryTimelineNodeView> CreateTimelineNodeViewAsync(ProjectLibraryTimelineNode node, CancellationToken cancellationToken)
    {
        var materials = await _evolutionLibrary!.GetMaterialReferencesAsync(Result.Project.Id, node.Id, cancellationToken);
        return new(
            node.Id,
            node.ObjectId,
            node.LocalDate,
            node.OccurredAt,
            node.Content,
            materials,
            ClassifyTimelineContext(materials));
    }

    private static LibraryTimelineContextKind ClassifyTimelineContext(
        IReadOnlyList<LibraryMaterialReference> materials)
    {
        var hasAuthorityProjection = materials.Any(value =>
            string.Equals(value.MaterialKind, LibraryProjectionMaterialKinds.AuthorityDecision, StringComparison.Ordinal));
        var hasAcceptedContribution = materials.Any(value =>
            string.Equals(value.MaterialKind, LibraryProjectionMaterialKinds.AcceptedContribution, StringComparison.Ordinal));
        if (hasAuthorityProjection && hasAcceptedContribution)
            return LibraryTimelineContextKind.B1AcceptedProjection;

        if (materials.Any(IsLegacyMaterial))
            return LibraryTimelineContextKind.LegacyContext;

        return LibraryTimelineContextKind.LibraryRecord;
    }

    private static bool IsLegacyMaterial(LibraryMaterialReference material) =>
        string.Equals(material.MaterialKind, LibraryProjectionMaterialKinds.LegacyContext, StringComparison.Ordinal) ||
        (material.Label?.StartsWith("Legacy ", StringComparison.OrdinalIgnoreCase) ?? false);
}
