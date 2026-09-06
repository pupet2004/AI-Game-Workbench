using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.Memory;
using Workbench.App.Services;
using Workbench.App.ViewModels;
using Workbench.Core.Continuity;
using Workbench.Project.Opening;
using Workbench.Storage.Continuity;

namespace Workbench.App.ProjectWorld;

public sealed record AcceptedStateItemView(
    string Statement,
    string Scope,
    string DecisionText,
    string SourceText);

public sealed record ActiveAssignmentItemView(
    AssignmentRef AssignmentRef,
    string AssignmentText,
    string ActorText,
    string ResponsibilityText,
    string RevisionText,
    string ContinuationText);

public sealed record DecisionItemView(
    string DecisionText,
    string EffectsText,
    string ContributionText);

public sealed record AttentionItemView(string Label, string Detail);

public sealed record LibraryContributionOptionView(
    AcceptedStateContributionRef ContributionRef,
    string Statement);

public partial class ProjectWorldExplorerViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly ProjectOpenResult _result;
    private readonly Func<Task> _backToHome;
    private readonly Func<AssignmentRef, Task> _beginManualWork;
    private readonly Func<Task> _openWorkspace;

    public ProjectWorldExplorerViewModel(
        AppServices services,
        ProjectOpenResult result,
        Func<Task> backToHome,
        Func<AssignmentRef, Task>? beginManualWork = null,
        Func<Task>? openWorkspace = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _backToHome = backToHome ?? throw new ArgumentNullException(nameof(backToHome));
        _beginManualWork = beginManualWork ?? (_ => Task.CompletedTask);
        _openWorkspace = openWorkspace ?? (() => Task.CompletedTask);
    }

    public string ProjectName => _result.Project.Name;
    public string ProjectPath => _result.Project.RootPath;
    public string UserPrincipal => _services.UserPrincipalProvider.GetCurrent().Value;
    public string ProjectStateLabel => HasAcceptedState
        ? LocalizationService.Current["Dynamic.ProjectAccepts"]
        : LocalizationService.Current["Explorer.NoAccepted"];

    public string AcceptedConstraintsSummary => string.Join("; ", AcceptedState.Select(item => item.Statement));
    public string ActiveAssignmentSummary => ActiveWork.Count == 0
        ? "No active assignments"
        : $"{ActiveWork.Count} active assignment{(ActiveWork.Count == 1 ? string.Empty : "s")}";
    public int PendingHandoffCount { get; private set; }
    public string PendingHandoffSummary => PendingHandoffCount == 0
        ? "No pending handoffs"
        : $"{PendingHandoffCount} pending handoff{(PendingHandoffCount == 1 ? string.Empty : "s")}";

    [ObservableProperty]
    public partial RecoveryViewModel? Recovery { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAcceptedState), nameof(ProjectStateLabel))]
    public partial bool HasAcceptedState { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(CanProjectToLibrary))]
    public partial bool Loading { get; set; }

    public bool IsBusy => Loading;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProjectToLibrary))]
    public partial LibraryContributionOptionView? SelectedLibraryContribution { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProjectToLibrary))]
    public partial string LibraryCategory { get; set; } = "Project";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProjectToLibrary))]
    public partial string LibraryTopic { get; set; } = "Current direction";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProjectToLibrary))]
    public partial string LibraryNodeContent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLibraryComposerVisible { get; set; }

    [ObservableProperty]
    public partial string? LibraryStatusMessage { get; set; }

    public ObservableCollection<AcceptedStateItemView> AcceptedState { get; } = [];
    public ObservableCollection<LibraryContributionOptionView> LibraryContributions { get; } = [];
    public ObservableCollection<ActiveAssignmentItemView> ActiveWork { get; } = [];
    public ObservableCollection<AttentionItemView> NeedsAttention { get; } = [];
    public ObservableCollection<DecisionItemView> RecentDecisions { get; } = [];

    public bool HasLegacyContext { get; private set; }
    public bool CanProjectToLibrary => !Loading && SelectedLibraryContribution is not null &&
        !string.IsNullOrWhiteSpace(LibraryCategory) &&
        !string.IsNullOrWhiteSpace(LibraryTopic) &&
        !string.IsNullOrWhiteSpace(LibraryNodeContent);
    public string LegacyContextText => HasLegacyContext
        ? LocalizationService.Current["Explorer.LegacyAvailable"]
        : LocalizationService.Current["Explorer.LegacyNone"];

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await ReloadAsync(cancellationToken);

    [RelayCommand]
    private async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (Loading)
            return;

        Loading = true;
        ErrorMessage = null;
        try
        {
            var projectRef = new ProjectRef(_result.Project.Id);
            var state = await _services.B1AuthorityRepository.LoadProjectStateAsync(projectRef, cancellationToken);
            var projection = B1Projector.Build(state);
            var library = await _services.LibraryAcceptedStateReader.ReadAsync(projectRef, cancellationToken);

            AcceptedState.Clear();
            LibraryContributions.Clear();
            foreach (var contribution in library.CurrentContributions)
            {
                LibraryContributions.Add(new(contribution.Contribution.ContributionRef, contribution.Contribution.Statement));
                AcceptedState.Add(new(
                    contribution.Contribution.Statement,
                    contribution.Contribution.Scope.ToString(),
                    $"Decision commit {contribution.Decision.ProjectCommitSequence}",
                    contribution.LibraryProjections.Count == 0
                        ? LocalizationService.Current["Dynamic.LibraryNoMapping"]
                        : string.Format(LocalizationService.Current["Dynamic.LibraryObjectsCount"], contribution.LibraryProjections.Count)));
            }
            SelectedLibraryContribution ??= LibraryContributions.FirstOrDefault();
            if (SelectedLibraryContribution is not null && string.IsNullOrWhiteSpace(LibraryNodeContent))
                LibraryNodeContent = SelectedLibraryContribution.Statement;

            ActiveWork.Clear();
            foreach (var assignment in projection.AcceptedProjectState.Assignments.Values)
            {
                var revisionRef = projection.AcceptedProjectState.CurrentEffectiveRevisionRefs[assignment.AssignmentRef];
                var revision = projection.AcceptedProjectState.Revisions[revisionRef];
                var actor = projection.AcceptedProjectState.LogicalActors[assignment.AssigneeActorRef];
                var responsibility = projection.AcceptedProjectState.Responsibilities[assignment.ResponsibilityRef];
                var continuation = projection.EffectiveCurrentAttemptRefs.TryGetValue(assignment.AssignmentRef, out var attempt) && attempt is not null
                    ? $"{LocalizationService.Current["Dynamic.AttemptSelected"]} {attempt.Value}"
                    : LocalizationService.Current["Dynamic.NotStarted"];
                ActiveWork.Add(new(
                    assignment.AssignmentRef,
                    assignment.AssignmentRef.ToString(),
                    $"{LocalizationService.Current["Dynamic.Actor"]} {actor.RoleKind}",
                    $"{LocalizationService.Current["Dynamic.Responsibility"]} {responsibility.Contract.Obligation}",
                    $"{LocalizationService.Current["Dynamic.Revision"]} {revision.RevisionRef}",
                    continuation));
            }

            OnPropertyChanged(nameof(AcceptedConstraintsSummary));
            OnPropertyChanged(nameof(ActiveAssignmentSummary));
            PendingHandoffCount = state.Handoffs.Count;
            OnPropertyChanged(nameof(PendingHandoffCount));
            OnPropertyChanged(nameof(PendingHandoffSummary));

            var completed = projection.AcceptedProjectState.RevisionDispositions.Values
                .Where(value => value.Disposition == AssignmentDisposition.Accepted)
                .Select(value => value.AssignmentRef)
                .ToHashSet();
            var remaining = projection.AcceptedProjectState.Assignments.Keys
                .Where(reference => !completed.Contains(reference))
                .Select(reference => reference.ToString())
                .ToArray();
            Recovery = new RecoveryViewModel(new RecoveryPresentationModel(
                AcceptedState.Select(item => item.Statement).ToArray(),
                completed.Select(reference => reference.ToString()).ToArray(),
                remaining,
                ["AcceptedProjectState", "Assignment projection", "Handoff projection"]));

            NeedsAttention.Clear();
            if (state.Handoffs.Count == 0)
            {
                NeedsAttention.Add(new(LocalizationService.Current["Dynamic.NoUnresolvedInputs"], LocalizationService.Current["Dynamic.NoHandoffAwaiting"]));
            }

            RecentDecisions.Clear();
            foreach (var decision in state.AuthorityDecisions.OrderByDescending(value => value.ProjectCommitSequence).Take(10))
            {
                var effects = new List<string>();
                if (decision.LogicalActorEstablishmentEffect is not null) effects.Add("LogicalActor");
                if (decision.ResponsibilityEstablishmentEffect is not null) effects.Add("Responsibility");
                if (decision.AssignmentDelegationEffect is not null) effects.Add(LocalizationService.Current["Dynamic.AssignmentRevision"]);
                if (decision.AssignmentDispositionEffect is not null) effects.Add("Disposition");
                if (decision.RevisionActivationEffect is not null) effects.Add(LocalizationService.Current["Dynamic.RevisionActivation"]);
                if (decision.AcceptedStateContributions.Count > 0) effects.Add(string.Format(LocalizationService.Current["Dynamic.AcceptedContributionsCount"], decision.AcceptedStateContributions.Count));
                RecentDecisions.Add(new(
                    $"Decision commit {decision.ProjectCommitSequence}",
                    effects.Count == 0 ? LocalizationService.Current["Dynamic.NoVisibleEffects"] : string.Join(", ", effects),
                    decision.AcceptedStateContributions.Count == 0
                        ? LocalizationService.Current["Dynamic.NoAcceptedStatements"]
                        : string.Join("; ", decision.AcceptedStateContributions.Select(value => value.Statement))));
            }

            HasAcceptedState = AcceptedState.Count > 0;
            HasLegacyContext = (await _services.B1ProjectGovernance.GetAsync(projectRef, cancellationToken))?.Origin == B1GovernanceOrigin.Adopted;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            Loading = false;
        }
    }

    [RelayCommand]
    private Task BackAsync() => _backToHome();

    [RelayCommand]
    private Task OpenWorkspaceAsync() => _openWorkspace();

    [RelayCommand]
    private Task BeginManualWorkAsync(AssignmentRef assignmentRef) => _beginManualWork(assignmentRef);

    [RelayCommand]
    private void ShowLibraryComposer()
    {
        if (LibraryContributions.Count == 0)
            return;

        SelectedLibraryContribution ??= LibraryContributions[0];
        if (string.IsNullOrWhiteSpace(LibraryNodeContent))
            LibraryNodeContent = SelectedLibraryContribution.Statement;
        LibraryStatusMessage = null;
        IsLibraryComposerVisible = true;
    }

    [RelayCommand]
    private async Task ProjectToLibraryAsync()
    {
        if (!CanProjectToLibrary)
            return;

        Loading = true;
        ErrorMessage = null;
        LibraryStatusMessage = null;
        try
        {
            var projectRef = new ProjectRef(_result.Project.Id);
            var proposal = await _services.ManualLibraryProjection.ProjectAsync(
                new ManualLibraryProjectionRequest(
                    projectRef,
                    SelectedLibraryContribution!.ContributionRef,
                    LibraryCategory,
                    LibraryTopic,
                    DateOnly.FromDateTime(_services.TimeProvider.GetUtcNow().UtcDateTime),
                    LibraryNodeContent));
            LibraryStatusMessage = string.Format(LocalizationService.Current["Dynamic.LibraryUpdated"], proposal.Draft.Category, proposal.Draft.Topic);
            IsLibraryComposerVisible = false;
            Loading = false;
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"{LocalizationService.Current["Dynamic.LibraryUpdateFailed"]} {exception.Message}";
        }
        finally
        {
            Loading = false;
        }
    }
}
