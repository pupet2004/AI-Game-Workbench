using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.Services;
using Workbench.App.ViewModels;
using Workbench.Core.Continuity;
using Workbench.Project.Opening;

namespace Workbench.App.ProjectWorld;

public sealed partial class ManualWorkViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly ProjectOpenResult _result;
    private readonly AssignmentRef _assignmentRef;
    private readonly Func<Task> _back;
    private readonly Func<HandoffRef, Task> _reviewHandoff;
    private HandoffRef? _currentHandoffRef;

    public ManualWorkViewModel(
        AppServices services,
        ProjectOpenResult result,
        AssignmentRef assignmentRef,
        Func<Task> back,
        Func<HandoffRef, Task>? reviewHandoff = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _assignmentRef = assignmentRef;
        _back = back ?? throw new ArgumentNullException(nameof(back));
        _reviewHandoff = reviewHandoff ?? (_ => Task.CompletedTask);
    }

    public string ProjectName => _result.Project.Name;
    public string UserPrincipal => _services.UserPrincipalProvider.GetCurrent().Value;

    [ObservableProperty] public partial string AssignmentText { get; private set; } = string.Empty;
    [ObservableProperty] public partial string ActorText { get; private set; } = string.Empty;
    [ObservableProperty] public partial string ResponsibilityText { get; private set; } = string.Empty;
    [ObservableProperty] public partial string RevisionText { get; private set; } = string.Empty;
    [ObservableProperty] public partial string AttemptText { get; private set; } = "No Attempt selected";
    [ObservableProperty] public partial string PrimaryResult { get; set; } = string.Empty;
    [ObservableProperty] public partial string ValidationsText { get; set; } = string.Empty;
    [ObservableProperty] public partial string UnresolvedIssuesText { get; set; } = string.Empty;
    [ObservableProperty] public partial string ProposedChangesText { get; set; } = string.Empty;
    [ObservableProperty] public partial string ProposedAssignmentRevision { get; set; } = string.Empty;
    [ObservableProperty] public partial string EvidenceReferencesText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsHandoffComposerVisible { get; set; }
    [ObservableProperty] public partial string? HandoffStatusMessage { get; set; }
    [ObservableProperty] public partial bool HasAttempt { get; private set; }
    [ObservableProperty] public partial bool HasHandoff { get; private set; }
    [ObservableProperty] public partial string HandoffText { get; private set; } = "No Handoff selected";
    [ObservableProperty] public partial bool IsBusy { get; private set; }
    [ObservableProperty] public partial string? ErrorMessage { get; private set; }

    public string WorkModeText => HasAttempt
        ? "Continue Manual Work"
        : "Begin Manual Work";

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await ReloadAsync(cancellationToken);

    [RelayCommand]
    private async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var projectRef = new ProjectRef(_result.Project.Id);
            var state = await _services.B1AuthorityRepository.LoadProjectStateAsync(projectRef, cancellationToken);
            var projection = B1Projector.Build(state);
            if (!projection.AcceptedProjectState.Assignments.TryGetValue(_assignmentRef, out var assignment))
                throw new B1CommandException(B1FailureCode.InvalidReference, "The Assignment is no longer part of the current Project World.");

            var revisionRef = projection.AcceptedProjectState.CurrentEffectiveRevisionRefs[assignment.AssignmentRef];
            var revision = projection.AcceptedProjectState.Revisions[revisionRef];
            var actor = projection.AcceptedProjectState.LogicalActors[assignment.AssigneeActorRef];
            var responsibility = projection.AcceptedProjectState.Responsibilities[assignment.ResponsibilityRef];
            AssignmentText = $"Assignment: {assignment.AssignmentRef}";
            ActorText = $"Acting as: {actor.RoleKind}";
            ResponsibilityText = $"Responsibility: {responsibility.Contract.Obligation}";
            RevisionText = $"Effective Revision: {revision.RevisionRef} · {revision.Contract.WorkContract}";

            HasAttempt = projection.EffectiveCurrentAttemptRefs.TryGetValue(assignment.AssignmentRef, out var selected) && selected is not null;
            AttemptText = HasAttempt
                ? $"Current Attempt: {selected!.Value}"
                : "No Attempt selected. Beginning work creates Attempt #1 and selects it explicitly.";
            OnPropertyChanged(nameof(WorkModeText));
            HandoffRef? handoff = null;
            if (HasAttempt)
                projection.EffectiveCurrentHandoffRefs.TryGetValue(selected!.Value, out handoff);
            HasHandoff = handoff is not null;
            _currentHandoffRef = HasHandoff ? handoff : null;
            HandoffText = HasHandoff ? $"Current Handoff: {handoff!.Value}" : "No Handoff selected";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Work could not be loaded; nothing was changed. {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BeginOrContinueAsync()
    {
        if (IsBusy)
            return;

        if (HasAttempt)
        {
            await ReloadAsync();
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var projectRef = new ProjectRef(_result.Project.Id);
            var state = await _services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
            var projection = B1Projector.Build(state);
            var revisionRef = projection.AcceptedProjectState.CurrentEffectiveRevisionRefs[_assignmentRef];
            projection.StoredAttemptSelections.TryGetValue(_assignmentRef, out var expectedStored);
            await _services.B1NonAuthoritativeCommands.CreateAttemptAndSelectAsync(
                new CreateAttemptCommand(
                    projectRef,
                    new UserPrincipalRef(UserPrincipal),
                    new AttemptRef(Guid.NewGuid()),
                    _assignmentRef,
                    revisionRef,
                    _services.TimeProvider.GetUtcNow()),
                expectedStored);
            IsBusy = false;
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Work could not be started; nothing was changed. {exception.Message}";
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ShowHandoffComposer()
    {
        if (HasAttempt)
        {
            IsHandoffComposerVisible = true;
            HandoffStatusMessage = null;
        }
    }

    [RelayCommand]
    private async Task RecordHandoffAsync()
    {
        if (!HasAttempt || string.IsNullOrWhiteSpace(PrimaryResult) || IsBusy)
            return;

        IsBusy = true;
        ErrorMessage = null;
        HandoffStatusMessage = null;
        try
        {
            var projectRef = new ProjectRef(_result.Project.Id);
            var state = await _services.B1AuthorityRepository.LoadProjectStateAsync(projectRef);
            var projection = B1Projector.Build(state);
            var attempt = projection.EffectiveCurrentAttemptRefs[_assignmentRef] ??
                throw new B1CommandException(B1FailureCode.InvalidReference, "No selected Attempt exists.");
            await _services.GuidedHandoffComposer.RecordAsync(
                attempt,
                _assignmentRef,
                new GuidedHandoffRequest(
                    projectRef,
                    new UserPrincipalRef(UserPrincipal),
                    PrimaryResult,
                    SplitLines(ValidationsText),
                    SplitLines(UnresolvedIssuesText),
                    SplitLines(ProposedChangesText),
                    string.IsNullOrWhiteSpace(ProposedAssignmentRevision) ? null : ProposedAssignmentRevision,
                    SplitLines(EvidenceReferencesText)));
            IsHandoffComposerVisible = false;
            HandoffStatusMessage = "Handoff recorded and selected as the current continuation. The project state is unchanged until a decision is recorded.";
            PrimaryResult = string.Empty;
            ValidationsText = string.Empty;
            UnresolvedIssuesText = string.Empty;
            ProposedChangesText = string.Empty;
            ProposedAssignmentRevision = string.Empty;
            EvidenceReferencesText = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Handoff could not be recorded; nothing was changed. {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static IReadOnlyList<string> SplitLines(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [RelayCommand]
    private Task BackAsync() => _back();

    [RelayCommand]
    private Task ReviewWorkResultAsync() =>
        _currentHandoffRef is { } handoffRef
            ? _reviewHandoff(handoffRef)
            : Task.CompletedTask;
}
