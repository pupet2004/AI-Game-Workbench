using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.Continuity;
using Workbench.App.Services;
using Workbench.App.ViewModels;
using Workbench.Core.Continuity;
using Workbench.Project.Opening;

namespace Workbench.App.ProjectWorld;

public sealed partial class GuidedDecisionViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly ProjectOpenResult _result;
    private readonly HandoffRef _handoffRef;
    private readonly Func<Task> _back;

    public GuidedDecisionViewModel(AppServices services, ProjectOpenResult result, HandoffRef handoffRef, Func<Task> back)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _handoffRef = handoffRef;
        _back = back ?? throw new ArgumentNullException(nameof(back));
        AvailableDispositions = new(Enum.GetValues<AssignmentDisposition>());
        AvailableContributionModes = new(Enum.GetValues<ContributionDecisionMode>());
    }

    public string ProjectName => _result.Project.Name;
    public string UserPrincipal => _services.UserPrincipalProvider.GetCurrent().Value;
    public ObservableCollection<AssignmentDisposition> AvailableDispositions { get; }
    public ObservableCollection<ContributionDecisionMode> AvailableContributionModes { get; }

    [ObservableProperty] public partial string HandoffText { get; private set; } = string.Empty;
    [ObservableProperty] public partial string SubmittedAsText { get; private set; } = string.Empty;
    [ObservableProperty] public partial string PrimaryResultText { get; private set; } = string.Empty;
    [ObservableProperty] public partial string ProposedContributionText { get; private set; } = string.Empty;
    [ObservableProperty] public partial AssignmentDisposition SelectedDisposition { get; set; } = AssignmentDisposition.Accepted;
    [ObservableProperty] public partial ContributionDecisionMode SelectedContributionMode { get; set; } = ContributionDecisionMode.Ignore;
    [ObservableProperty] public partial string EditedContributionStatement { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewRevisionContract { get; set; } = string.Empty;
    [ObservableProperty] public partial string? PreviewText { get; private set; }
    [ObservableProperty] public partial bool IsPreviewVisible { get; private set; }
    [ObservableProperty] public partial bool IsBusy { get; private set; }
    [ObservableProperty] public partial string? ErrorMessage { get; private set; }
    [ObservableProperty] public partial string? StatusMessage { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var state = await _services.B1AuthorityRepository.LoadProjectStateAsync(new ProjectRef(_result.Project.Id), cancellationToken);
        var handoff = state.Handoffs.SingleOrDefault(value => value.HandoffRef == _handoffRef)
            ?? throw new B1CommandException(B1FailureCode.InvalidReference, "The Handoff no longer exists.");
        var claims = state.Claims.ToDictionary(value => value.ClaimRef);
        HandoffText = $"Handoff: {handoff.HandoffRef}";
        SubmittedAsText = handoff.ResultClaimRef is { } resultRef && claims.TryGetValue(resultRef, out var resultClaim)
            ? $"Submitted as: {resultClaim.ClaimantRef}"
            : "Submitted as: LogicalActor";
        PrimaryResultText = resultClaim?.Payload is ClaimPayload.Result result ? result.Statement : "";
        var proposed = handoff.ProposedContributionClaimRefs
            .Where(claims.ContainsKey)
            .Select(value => claims[value].Payload)
            .OfType<ClaimPayload.ProposedStateContribution>()
            .Select(value => value.Statement)
            .ToArray();
        ProposedContributionText = proposed.Length == 0 ? "No proposed Project contribution." : string.Join("; ", proposed);
    }

    [RelayCommand]
    private async Task PreviewDecisionAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var preview = await _services.GuidedDecision.PreviewAsync(BuildRequest());
            PreviewText = $"Deciding as: {preview.DecidingAs}\nDisposition: {preview.Disposition}\nEffects: {string.Join(", ", preview.Effects)}\n\n{preview.ContributionSummary}";
            IsPreviewVisible = true;
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Preview could not be created; nothing was changed. {exception.Message}";
            IsPreviewVisible = false;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ConfirmDecisionAsync()
    {
        if (!IsPreviewVisible || IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var decision = await _services.GuidedDecision.CommitAsync(BuildRequest());
            StatusMessage = $"Decision recorded (project event {decision.ProjectCommitSequence}). The overview and library view have been refreshed.";
            IsPreviewVisible = false;
        }
        catch (Exception exception) { ErrorMessage = $"Decision could not be recorded; nothing was changed. {exception.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private Task BackAsync() => _back();

    private GuidedDecisionRequest BuildRequest() => new(
        new ProjectRef(_result.Project.Id),
        new UserPrincipalRef(UserPrincipal),
        _handoffRef,
        SelectedDisposition,
        SelectedContributionMode,
        string.IsNullOrWhiteSpace(EditedContributionStatement) ? null : EditedContributionStatement,
        string.IsNullOrWhiteSpace(NewRevisionContract) ? null : NewRevisionContract);
}
