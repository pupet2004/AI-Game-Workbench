using Workbench.App.Memory;
using Workbench.Core.Continuity;
using Workbench.Storage.Continuity;
using Workbench.Storage.Memory;

namespace Workbench.App.ProjectWorld;

public sealed record ManualLibraryProjectionRequest(
    ProjectRef ProjectRef,
    AcceptedStateContributionRef ContributionRef,
    string Category,
    string Topic,
    DateOnly LocalDate,
    string NodeContent);

public sealed class ManualLibraryProjectionService(
    B1AuthorityRepository authorityRepository,
    LibraryProjectionContractService projectionContracts,
    IProjectMemoryApi projectMemoryApi,
    ProjectLibraryProposalService proposals,
    TimeProvider timeProvider)
{
    private readonly B1AuthorityRepository _authorityRepository = authorityRepository ?? throw new ArgumentNullException(nameof(authorityRepository));
    private readonly LibraryProjectionContractService _projectionContracts = projectionContracts ?? throw new ArgumentNullException(nameof(projectionContracts));
    private readonly IProjectMemoryApi _projectMemoryApi = projectMemoryApi ?? throw new ArgumentNullException(nameof(projectMemoryApi));
    private readonly ProjectLibraryProposalService _proposals = proposals ?? throw new ArgumentNullException(nameof(proposals));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<ProjectLibraryProposal> ProjectAsync(
        ManualLibraryProjectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Category);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.NodeContent);

        var state = await _authorityRepository.LoadProjectStateAsync(request.ProjectRef, cancellationToken);
        var decision = state.AuthorityDecisions.SingleOrDefault(value =>
            value.AcceptedStateContributions.Any(contribution => contribution.ContributionRef == request.ContributionRef));
        if (decision is null)
            throw new InvalidOperationException("The selected project statement is no longer available for Library update.");

        var contribution = decision.AcceptedStateContributions.Single(value => value.ContributionRef == request.ContributionRef);
        var sourceClaim = contribution.SourceClaimRef;
        var sourceHandoff = sourceClaim is { } claim
            ? state.Handoffs.LastOrDefault(value => value.ProposedContributionClaimRefs.Contains(claim))?.HandoffRef
            : null;
        var now = _timeProvider.GetUtcNow();
        var proposal = await _projectionContracts.CreateProposalAsync(new(
            Guid.NewGuid(),
            request.ProjectRef,
            LibraryProposalAction.CreateNode,
            null,
            null,
            null,
            null,
            request.Category,
            request.Topic,
            request.LocalDate,
            request.NodeContent,
            null,
            new(
                decision.DecisionRef,
                contribution.ContributionRef,
                sourceClaim,
                sourceHandoff,
                null,
                []),
            now), cancellationToken);

        await _projectMemoryApi.AcceptLibraryProposalAsync(request.ProjectRef.Value, proposal.Id, cancellationToken);
        return await _proposals.GetAsync(request.ProjectRef.Value, proposal.Id, cancellationToken)
            ?? throw new InvalidOperationException("The Library update could not be reloaded.");
    }
}
