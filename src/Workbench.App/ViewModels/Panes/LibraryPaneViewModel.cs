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
    string Content,
    IReadOnlyList<LibraryMaterialReference> Materials,
    LibraryTimelineContextKind ContextKind)
{
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
    string Text)
{
    public string KindLabel => Kind switch
    {
        SummaryDeltaKind.Decision => LocalizationService.Current["Library.Decision"],
        SummaryDeltaKind.Change => LocalizationService.Current["Library.Change"],
        SummaryDeltaKind.Constraint => LocalizationService.Current["Library.Constraint"],
        SummaryDeltaKind.RejectedPath => LocalizationService.Current["Library.RejectedPath"],
        _ => LocalizationService.Current["Library.Unresolved"]
    };
}

public sealed record LibraryTimeDayView(
    DateOnly LocalDate,
    DailySummaryDocument? Summary,
    IReadOnlyList<LibrarySummaryEntryView> SummaryEntries,
    IReadOnlyList<LibraryTimeGroupView> Groups);

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
    private readonly Dictionary<Guid, string> _candidateSourceLabels = [];
    private readonly ProjectLibraryRepository? _library;
    private readonly ProjectLibraryEvolutionRepository? _evolutionLibrary;
    private readonly IProjectMemoryApi? _projectMemoryApi;
    private readonly ProjectSummaryRepository? _projectSummaryRepository;
    private readonly LibraryAcceptedStateReader? _acceptedStateReader;

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
    public partial LibrarySection SelectedSection { get; set; } = LibrarySection.Category;

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
    [ObservableProperty] public partial string? SelectedCategory { get; set; }
    public ObservableCollection<ProjectLibraryObject> SelectedCategoryObjects { get; } = [];
    public ObservableCollection<LibraryTimelineNodeView> ObjectTimeline { get; } = [];
    public ObservableCollection<DateOnly> TimeDates { get; } = [];
    public ObservableCollection<LibraryTimeGroupView> TimeGroups { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelectedTimeDay))] public partial LibraryTimeDayView? SelectedTimeDay { get; set; }
    public bool HasSelectedTimeDay => SelectedTimeDay is not null;
    public ObservableCollection<LibraryTimeDayView> TimeDays { get; } = [];
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

    public async Task ShowCategoryAsync(CancellationToken cancellationToken = default)
    {
        SelectedSection = LibrarySection.Category;
        await LoadLibraryAsync(cancellationToken);
    }

    public async Task ShowOverviewAsync(CancellationToken cancellationToken = default)
    {
        if (AcceptedStateReadModel is null && _acceptedStateReader is not null)
            await LoadAcceptedStateAsync(cancellationToken);
        SelectedSection = AcceptedStateReadModel is null ? LibrarySection.Category : LibrarySection.Overview;
    }

    public async Task ShowTimeAsync(CancellationToken cancellationToken = default)
    {
        SelectedSection = LibrarySection.Time;
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
            return Task.CompletedTask;
        }

        SelectedTimeDay = TimeDays.FirstOrDefault(value => value.LocalDate == localDate);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task SelectTimeDate(DateOnly localDate) => SelectTimeDateAsync(localDate);

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
                     .Concat(summaryEntries.Select(entry => DateOnly.FromDateTime(entry.OccurredAt.LocalDateTime)))
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
        foreach (var date in TimeDates)
        {
            var summary = _projectMemoryApi is null
                ? null
                : await _projectMemoryApi.GetDailySummaryAsync(Result.Project.Id, date, cancellationToken);
            var entries = summaryEntries
                .Where(entry => DateOnly.FromDateTime(entry.OccurredAt.LocalDateTime) == date)
                .OrderBy(entry => entry.OccurredAt)
                .Select(entry => new LibrarySummaryEntryView(entry.OccurredAt, entry.Kind, entry.Text))
                .ToArray();
            var groups = TimeGroups.Where(group => group.LocalDate == date).ToArray();
            TimeDays.Add(new(date, summary, entries, groups));
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
