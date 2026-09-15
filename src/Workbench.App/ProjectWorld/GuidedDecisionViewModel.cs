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
    private readonly Func<Task>? _afterCommit;
    private GuidedDecisionRequest? _previewedRequest;

    public GuidedDecisionViewModel(
        AppServices services,
        ProjectOpenResult result,
        HandoffRef handoffRef,
        Func<Task> back,
        Func<Task>? afterCommit = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _handoffRef = handoffRef;
        _back = back ?? throw new ArgumentNullException(nameof(back));
        _afterCommit = afterCommit;
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
    [ObservableProperty] public partial string VerificationSummaryText { get; private set; } = string.Empty;
    [ObservableProperty] public partial string EvidenceSummaryText { get; private set; } = string.Empty;
    [ObservableProperty] public partial AssignmentDisposition SelectedDisposition { get; set; } = AssignmentDisposition.Accepted;
    [ObservableProperty] public partial ContributionDecisionMode SelectedContributionMode { get; set; } = ContributionDecisionMode.AdoptVerbatim;
    [ObservableProperty] public partial string EditedContributionStatement { get; set; } = string.Empty;
    [ObservableProperty] public partial string NewRevisionContract { get; set; } = string.Empty;
    [ObservableProperty] public partial string SuccessorAssignmentContract { get; set; } = string.Empty;
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
        HandoffText = LocalizationService.Current["Decision.CompletedByWorker"];
        SubmittedAsText = handoff.ResultClaimRef is { } resultRef && claims.TryGetValue(resultRef, out var resultClaim)
            ? $"{LocalizationService.Current["Dynamic.SubmittedAs"]} {resultClaim.ClaimantRef}"
            : LocalizationService.Current["Dynamic.SubmittedLogicalActor"];
        PrimaryResultText = resultClaim?.Payload is ClaimPayload.Result result ? result.Statement : "";
        var proposed = handoff.ProposedContributionClaimRefs
            .Where(claims.ContainsKey)
            .Select(value => claims[value].Payload)
            .OfType<ClaimPayload.ProposedStateContribution>()
            .Select(value => value.Statement)
            .ToArray();
        ProposedContributionText = proposed.Length == 0 ? LocalizationService.Current["Dynamic.NoContribution"] : string.Join("; ", proposed);
        var validations = handoff.ValidationClaimRefs
            .Where(claims.ContainsKey)
            .Select(value => claims[value].Payload)
            .OfType<ClaimPayload.Validation>()
            .Select(value => value.Statement)
            .ToArray();
        VerificationSummaryText = validations.Length == 0
            ? LocalizationService.Current["Review.NoVerification"]
            : string.Join("\n", validations);
        EvidenceSummaryText = handoff.EvidenceRefs.Count == 0
            ? LocalizationService.Current["Review.NoEvidence"]
            : string.Format(LocalizationService.Current["Review.EvidenceCount"], handoff.EvidenceRefs.Count);
    }

    [RelayCommand]
    private async Task PreviewDecisionAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        InvalidatePreview();
        try
        {
            var request = BuildRequest();
            var preview = await _services.GuidedDecision.PreviewAsync(request);
            if (request != BuildRequest()) return;
            _previewedRequest = request;
            PreviewText = BuildPreviewText(request, preview);
            IsPreviewVisible = true;
        }
        catch (Exception exception)
        {
            ErrorMessage = $"{LocalizationService.Current["Dynamic.DecisionPreviewFailed"]} {exception.Message}";
            IsPreviewVisible = false;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private Task AcceptAsync() =>
        PreviewProductDecisionAsync(AssignmentDisposition.Accepted, ContributionDecisionMode.AdoptVerbatim);

    [RelayCommand]
    private Task RequestRevisionAsync() =>
        PreviewProductDecisionAsync(AssignmentDisposition.RevisionRequired, ContributionDecisionMode.Ignore);

    [RelayCommand]
    private Task RejectAsync() =>
        PreviewProductDecisionAsync(AssignmentDisposition.Rejected, ContributionDecisionMode.Ignore);

    private async Task PreviewProductDecisionAsync(
        AssignmentDisposition disposition,
        ContributionDecisionMode contributionMode)
    {
        if (IsBusy) return;
        SelectedDisposition = disposition;
        SelectedContributionMode = contributionMode;
        await PreviewDecisionAsync();
    }

    [RelayCommand]
    private async Task ConfirmDecisionAsync()
    {
        if (!IsPreviewVisible || IsBusy || _previewedRequest is not { } request) return;
        if (request != BuildRequest())
        {
            InvalidatePreview();
            return;
        }
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var decision = await _services.GuidedDecision.CommitAsync(request);
            StatusMessage = request.ContributionMode == ContributionDecisionMode.Ignore
                ? string.Format(LocalizationService.Current["Dynamic.DecisionRecordedNoContribution"], decision.ProjectCommitSequence)
                : string.Format(LocalizationService.Current["Dynamic.DecisionRecorded"], decision.ProjectCommitSequence);
            InvalidatePreview();
            if (_afterCommit is not null)
                await _afterCommit();
        }
        catch (Exception exception) { ErrorMessage = $"{LocalizationService.Current["Dynamic.DecisionFailed"]} {exception.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private Task BackAsync() => _back();

    partial void OnSelectedDispositionChanged(AssignmentDisposition value) => InvalidatePreview();
    partial void OnSelectedContributionModeChanged(ContributionDecisionMode value) => InvalidatePreview();
    partial void OnEditedContributionStatementChanged(string value) => InvalidatePreview();
    partial void OnNewRevisionContractChanged(string value) => InvalidatePreview();
    partial void OnSuccessorAssignmentContractChanged(string value) => InvalidatePreview();

    private void InvalidatePreview()
    {
        _previewedRequest = null;
        IsPreviewVisible = false;
        PreviewText = null;
    }

    private static string BuildPreviewText(GuidedDecisionRequest request, GuidedDecisionPreview preview)
    {
        var labels = LocalizationService.Current;
        var lines = new List<string>
        {
            labels[request.Disposition switch
            {
                AssignmentDisposition.Accepted => "Decision.AcceptPreview",
                AssignmentDisposition.RevisionRequired => "Decision.RevisePreview",
                _ => "Decision.RejectPreview"
            }]
        };
        lines.Add(request.Disposition == AssignmentDisposition.Accepted &&
                  request.ContributionMode != ContributionDecisionMode.Ignore
            ? preview.ContributionSummary
            : labels["Decision.StateUnchanged"]);
        if (request.Disposition == AssignmentDisposition.RevisionRequired && request.NewRevisionContract is not null)
            lines.Add($"{labels["Decision.RevisionWork"]}\n{request.NewRevisionContract}");
        if (request.Disposition == AssignmentDisposition.Accepted && request.SuccessorAssignmentContract is not null)
            lines.Add($"{labels["Decision.NextWork"]}\n{request.SuccessorAssignmentContract}");
        return string.Join("\n\n", lines);
    }

    private GuidedDecisionRequest BuildRequest() => new(
        new ProjectRef(_result.Project.Id),
        new UserPrincipalRef(UserPrincipal),
        _handoffRef,
        SelectedDisposition,
        SelectedContributionMode,
        string.IsNullOrWhiteSpace(EditedContributionStatement) ? null : EditedContributionStatement,
        string.IsNullOrWhiteSpace(NewRevisionContract) ? null : NewRevisionContract,
        string.IsNullOrWhiteSpace(SuccessorAssignmentContract) ? null : SuccessorAssignmentContract);
}
