using Workbench.Core.Continuity;
using Workbench.Storage.Continuity;
using Workbench.Storage.Memory;

namespace Workbench.App.Memory;

/// <summary>
/// Validates the boundary between B1 authority and the user-facing Library.
/// It only reads B1/Library state and returns a validated application object.
/// It deliberately does not write either AcceptedProjectState or Library data.
/// </summary>
public sealed class LibraryProjectionContractService(
    B1AuthorityRepository authorityRepository,
    ProjectLibraryEvolutionRepository libraryRepository,
    ProjectLibraryProposalService? proposalService = null)
{
    private readonly B1AuthorityRepository _authorityRepository =
        authorityRepository ?? throw new ArgumentNullException(nameof(authorityRepository));
    private readonly ProjectLibraryEvolutionRepository _libraryRepository =
        libraryRepository ?? throw new ArgumentNullException(nameof(libraryRepository));
    private readonly ProjectLibraryProposalService? _proposalService = proposalService;

    public async Task<ProjectLibraryProposal> CreateProposalAsync(
        LibraryProjectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_proposalService is null)
            throw new InvalidOperationException("Library projection proposal persistence is unavailable.");

        var validated = await ValidateAsync(request, cancellationToken);
        var draft = new ProjectLibraryProposalDraft(
            request.ProposalId,
            request.ProjectRef.Value,
            null,
            request.Action,
            request.TargetObjectId,
            request.TargetNodeId,
            request.ExpectedNodeRevision,
            request.ExpectedOverviewRevision,
            request.Category,
            request.Topic,
            request.LocalDate,
            request.NodeContent,
            request.CurrentOverview,
            validated.MaterialReferences,
            request.CreatedAt,
            request.Provenance.AuthorityDecisionRef.Value,
            request.Provenance.ContributionRef.Value,
            request.Provenance.SourceClaimRef?.Value,
            request.Provenance.SourceHandoffRef?.Value,
            request.Provenance.SummaryRef);
        return await _proposalService.CreateProposalAsync(draft, cancellationToken);
    }

    public async Task<ValidatedLibraryProjection> ValidateAsync(
        LibraryProjectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateShape(request);

        var state = await _authorityRepository.LoadProjectStateAsync(request.ProjectRef, cancellationToken);
        var decision = state.AuthorityDecisions.SingleOrDefault(
            value => value.DecisionRef == request.Provenance.AuthorityDecisionRef);
        if (decision is null)
        {
            throw new InvalidOperationException(
                "The Library projection must reference an AuthorityDecision owned by this project.");
        }

        var contribution = decision.AcceptedStateContributions.SingleOrDefault(
            value => value.ContributionRef == request.Provenance.ContributionRef);
        if (contribution is null)
        {
            throw new InvalidOperationException(
                "The Library projection must reference an accepted contribution produced by its AuthorityDecision.");
        }

        ValidateOptionalClaim(state, request.Provenance.SourceClaimRef, contribution, decision);
        ValidateOptionalHandoff(state, request.Provenance.SourceHandoffRef, decision);

        if (request.TargetObjectId is { } objectId)
        {
            var libraryObject = await _libraryRepository.GetObjectAsync(
                request.ProjectRef.Value,
                objectId,
                cancellationToken);
            if (request.Action == LibraryProposalAction.UpdateNode && libraryObject is null)
            {
                throw new InvalidOperationException(
                    "An UpdateNode Library projection must target an existing Object owned by this project.");
            }
        }

        if (request.TargetNodeId is { } nodeId)
        {
            var node = await _libraryRepository.GetNodeAsync(
                request.ProjectRef.Value,
                nodeId,
                cancellationToken);
            if (node is null || request.TargetObjectId != node.ObjectId)
            {
                throw new InvalidOperationException(
                    "The Library Timeline Node is missing, cross-project, or does not belong to the target Object.");
            }
        }

        var materials = AddProvenanceReferences(request.Provenance);
        return new(request, decision, contribution, materials);
    }

    private static void ValidateShape(LibraryProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProposalId == Guid.Empty) throw new ArgumentException("Proposal identity is required.", nameof(request));
        if (request.ProjectRef.Value == Guid.Empty) throw new ArgumentException("Project identity is required.", nameof(request));
        if (!Enum.IsDefined(request.Action)) throw new ArgumentOutOfRangeException(nameof(request.Action));
        if (request.TargetObjectId == Guid.Empty || request.TargetNodeId == Guid.Empty)
            throw new ArgumentException("Target identities must be non-empty when supplied.", nameof(request));
        if (request.LocalDate == DateOnly.MinValue) throw new ArgumentException("Local date is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Category)) throw new ArgumentException("Category is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Topic)) throw new ArgumentException("Topic is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.NodeContent)) throw new ArgumentException("Node content is required.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.Provenance);
        ArgumentNullException.ThrowIfNull(request.Provenance.Materials);

        if (request.Action == LibraryProposalAction.CreateNode &&
            (request.TargetNodeId is not null || request.ExpectedNodeRevision is not null))
        {
            throw new ArgumentException("CreateNode cannot target an existing Timeline Node.", nameof(request));
        }

        if (request.Action == LibraryProposalAction.UpdateNode &&
            (request.TargetObjectId is null || request.TargetNodeId is null || request.ExpectedNodeRevision is not >= 1))
        {
            throw new ArgumentException("UpdateNode requires an Object, Timeline Node, and positive expected revision.", nameof(request));
        }

        if (request.CurrentOverview is not null && request.ExpectedOverviewRevision is not >= 0)
        {
            throw new ArgumentException("An Overview replacement requires an expected overview revision.", nameof(request));
        }
    }

    private static void ValidateOptionalClaim(
        B1ProjectState state,
        ClaimRef? claimRef,
        AcceptedStateContribution contribution,
        AuthorityDecision decision)
    {
        if (claimRef is null) return;
        if (!state.Claims.Any(value => value.ClaimRef == claimRef.Value))
            throw new InvalidOperationException("The source Claim is not owned by this project.");
        if (contribution.SourceClaimRef != claimRef &&
            !decision.ConsideredRefs.Contains(new ConsideredRef.Claim(claimRef.Value)))
        {
            throw new InvalidOperationException(
                "The source Claim was not the accepted contribution source or a considered Decision input.");
        }
    }

    private static void ValidateOptionalHandoff(
        B1ProjectState state,
        HandoffRef? handoffRef,
        AuthorityDecision decision)
    {
        if (handoffRef is null) return;
        if (!state.Handoffs.Any(value => value.HandoffRef == handoffRef.Value))
            throw new InvalidOperationException("The source Handoff is not owned by this project.");
        if (!decision.ConsideredRefs.Contains(new ConsideredRef.Handoff(handoffRef.Value)))
        {
            throw new InvalidOperationException(
                "The source Handoff was not recorded as considered by the AuthorityDecision.");
        }
    }

    private static IReadOnlyList<LibraryMaterialReferenceDraft> AddProvenanceReferences(
        LibraryProjectionProvenance provenance)
    {
        var result = new List<LibraryMaterialReferenceDraft>();
        foreach (var material in provenance.Materials)
        {
            ArgumentNullException.ThrowIfNull(material);
            result.Add(material);
        }

        AddUnique(result, "AuthorityDecision", provenance.AuthorityDecisionRef.Value.ToString(), "Accepted by B1 AuthorityDecision");
        AddUnique(result, "AcceptedContribution", provenance.ContributionRef.Value.ToString(), "Accepted B1 contribution");
        if (provenance.SourceClaimRef is { } claim)
            AddUnique(result, "Claim", claim.Value.ToString(), "Accepted contribution source Claim");
        if (provenance.SourceHandoffRef is { } handoff)
            AddUnique(result, "Handoff", handoff.Value.ToString(), "Considered Handoff");
        if (!string.IsNullOrWhiteSpace(provenance.SummaryRef))
            AddUnique(result, "Summary", provenance.SummaryRef, "Non-authoritative continuity Summary");
        return result;
    }

    private static void AddUnique(
        ICollection<LibraryMaterialReferenceDraft> materials,
        string kind,
        string reference,
        string label)
    {
        if (!materials.Any(value =>
                string.Equals(value.MaterialKind, kind, StringComparison.Ordinal) &&
                string.Equals(value.Reference, reference, StringComparison.Ordinal)))
        {
            materials.Add(new(kind, reference, label));
        }
    }
}
