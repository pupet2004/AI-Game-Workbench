using Workbench.Core.Continuity;
using Workbench.Storage.Memory;

namespace Workbench.App.Memory;

public static class LibraryProjectionMaterialKinds
{
    public const string AuthorityDecision = "AuthorityDecision";
    public const string AcceptedContribution = "AcceptedContribution";
    public const string LegacyContext = "LegacyContext";
    public const string Claim = "Claim";
    public const string Handoff = "Handoff";
    public const string Summary = "Summary";
}

public sealed record LibraryObjectTimelineProjection(
    ProjectLibraryObject Object,
    ProjectLibraryTimelineNode Node,
    IReadOnlyList<LibraryMaterialReference> Materials,
    AuthorityDecisionRef AuthorityDecisionRef,
    AcceptedStateContributionRef ContributionRef);

public sealed record LibraryAcceptedContributionProjection(
    AcceptedStateContribution Contribution,
    AuthorityDecision Decision,
    IReadOnlyList<LibraryObjectTimelineProjection> LibraryProjections);

public sealed record LibraryProjectDecisionProjection(
    AuthorityDecision Decision,
    IReadOnlyList<AcceptedStateContribution> Contributions,
    IReadOnlyList<LibraryObjectTimelineProjection> LibraryProjections);

public sealed record LibraryAcceptedStateReadModel(
    ProjectRef ProjectRef,
    AcceptedProjectState AcceptedProjectState,
    IReadOnlyList<LibraryAcceptedContributionProjection> CurrentContributions,
    IReadOnlyList<LibraryProjectDecisionProjection> Decisions,
    IReadOnlyList<LibraryProjectDecisionProjection> ProjectLevelDecisions);
