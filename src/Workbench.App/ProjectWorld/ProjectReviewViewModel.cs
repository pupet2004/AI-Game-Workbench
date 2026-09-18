using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.Services;
using Workbench.App.ViewModels;
using Workbench.Core.Continuity;
using Workbench.Project.Opening;

namespace Workbench.App.ProjectWorld;

public sealed partial class ProjectReviewViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly ProjectOpenResult _result;
    private readonly Func<Task> _backToOverview;
    private readonly Func<Task> _backToWorkspace;
    private readonly Func<HandoffRef, Task> _openGuidedDecision;

    public ProjectReviewViewModel(
        AppServices services,
        ProjectOpenResult result,
        Func<Task> backToOverview,
        Func<Task> backToWorkspace,
        Func<HandoffRef, Task> openGuidedDecision)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _backToOverview = backToOverview ?? throw new ArgumentNullException(nameof(backToOverview));
        _backToWorkspace = backToWorkspace ?? throw new ArgumentNullException(nameof(backToWorkspace));
        _openGuidedDecision = openGuidedDecision ?? throw new ArgumentNullException(nameof(openGuidedDecision));
    }

    public string ProjectName => _result.Project.Name;
    public string ProjectPath => _result.Project.RootPath;
    public string UserPrincipal => _services.UserPrincipalProvider.GetCurrent().Value;
    public int PendingHandoffCount { get; private set; }
    public string PendingHandoffSummary =>
        PendingHandoffCount == 0
            ? LocalizationService.Current["Review.NoPending"]
            : string.Format(LocalizationService.Current["Review.PendingCount"], PendingHandoffCount);

    [ObservableProperty]
    public partial bool Loading { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    public ObservableCollection<PendingHandoffItemView> PendingHandoffs { get; } = [];

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
            var state = await _services.B1AuthorityRepository.LoadProjectStateAsync(
                new ProjectRef(_result.Project.Id),
                cancellationToken);
            var considered = state.AuthorityDecisions
                .SelectMany(value => value.ConsideredRefs)
                .OfType<ConsideredRef.Handoff>()
                .Select(value => value.HandoffRef)
                .ToHashSet();
            var claims = state.Claims.ToDictionary(value => value.ClaimRef);

            PendingHandoffs.Clear();
            foreach (var handoff in state.Handoffs
                .Where(value => !considered.Contains(value.HandoffRef))
                .OrderByDescending(value => value.CreatedAt))
            {
                PendingHandoffs.Add(new(
                    handoff.HandoffRef,
                    ClaimStatement(claims, handoff.ResultClaimRef),
                    JoinClaimStatements(claims, handoff.ProposedContributionClaimRefs),
                    handoff.ValidationClaimRefs.Count == 0
                        ? LocalizationService.Current["Review.NoVerification"]
                        : JoinClaimStatements(claims, handoff.ValidationClaimRefs),
                    handoff.EvidenceRefs.Count == 0
                        ? LocalizationService.Current["Review.NoEvidence"]
                        : string.Format(LocalizationService.Current["Review.EvidenceCount"], handoff.EvidenceRefs.Count),
                    LocalizationService.Current["Review.WaitingForDecision"]));
            }

            PendingHandoffCount = PendingHandoffs.Count;
            OnPropertyChanged(nameof(PendingHandoffCount));
            OnPropertyChanged(nameof(PendingHandoffSummary));
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
    private Task BackAsync() => _backToOverview();

    [RelayCommand]
    private Task BackToWorkspaceAsync() => _backToWorkspace();

    [RelayCommand]
    private Task ReviewHandoffAsync(HandoffRef handoffRef) => _openGuidedDecision(handoffRef);

    private static string ClaimStatement(
        IReadOnlyDictionary<ClaimRef, Claim> claims,
        ClaimRef claimRef) =>
        claims.TryGetValue(claimRef, out var claim)
            ? claim.Payload switch
            {
                ClaimPayload.Result result => result.Statement,
                ClaimPayload.Validation validation => validation.Statement,
                ClaimPayload.UnresolvedIssue issue => issue.Statement,
                ClaimPayload.ProposedStateContribution contribution => contribution.Statement,
                ClaimPayload.ProposedAssignmentRevision revision => revision.ProposedContract.WorkContract,
                _ => claimRef.ToString()
            }
            : $"Missing claim {claimRef}";

    private static string JoinClaimStatements(
        IReadOnlyDictionary<ClaimRef, Claim> claims,
        IReadOnlyList<ClaimRef> claimRefs) =>
        claimRefs.Count == 0
            ? LocalizationService.Current["Dynamic.NoneReported"]
            : string.Join("; ", claimRefs.Select(value => ClaimStatement(claims, value)));
}
