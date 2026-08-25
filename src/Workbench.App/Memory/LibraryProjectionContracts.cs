using Workbench.Core.Continuity;
using Workbench.Storage.Memory;

namespace Workbench.App.Memory;

/// <summary>
/// The explicit provenance required when an accepted B1 contribution is offered
/// for display in the user-facing Library. This is an application contract;
/// it does not add a B1 contribution scope or write AcceptedProjectState.
/// </summary>
public sealed record LibraryProjectionProvenance(
    AuthorityDecisionRef AuthorityDecisionRef,
    AcceptedStateContributionRef ContributionRef,
    ClaimRef? SourceClaimRef,
    HandoffRef? SourceHandoffRef,
    string? SummaryRef,
    IReadOnlyList<LibraryMaterialReferenceDraft> Materials);

/// <summary>
/// A request to project one accepted contribution into a Library Object/Timeline.
/// Object association is explicit. A caller that cannot identify an Object must
/// keep the event at Project Decision level instead of submitting this request.
/// </summary>
public sealed record LibraryProjectionRequest(
    Guid ProposalId,
    ProjectRef ProjectRef,
    LibraryProposalAction Action,
    Guid? TargetObjectId,
    Guid? TargetNodeId,
    int? ExpectedNodeRevision,
    int? ExpectedOverviewRevision,
    string Category,
    string Topic,
    DateOnly LocalDate,
    string NodeContent,
    string? CurrentOverview,
    LibraryProjectionProvenance Provenance,
    DateTimeOffset CreatedAt);

public sealed record ValidatedLibraryProjection(
    LibraryProjectionRequest Request,
    AuthorityDecision Decision,
    AcceptedStateContribution Contribution,
    IReadOnlyList<LibraryMaterialReferenceDraft> MaterialReferences);
