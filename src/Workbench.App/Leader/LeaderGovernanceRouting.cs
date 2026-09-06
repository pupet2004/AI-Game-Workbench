using Workbench.Core.Continuity;

namespace Workbench.App.Leader;

/// <summary>
/// An ephemeral, user-facing suggestion derived from an Evolution Candidate.
/// It is deliberately not a persisted proposal or decision.
/// </summary>
public sealed record LeaderLibraryProposalDraftSuggestion(
    string Category,
    string Topic,
    string NodeContent);

public sealed record LeaderGovernanceRouteSuggestion(
    LeaderEvolutionCandidate Candidate,
    LeaderEvolutionRouteHint RouteHint,
    bool IsSafe,
    string? SafetyNote,
    AuthorityConfirmationDraft? AuthorityConfirmation,
    LeaderLibraryProposalDraftSuggestion? LibraryProposal)
{
    public bool HasDraft => AuthorityConfirmation is not null || LibraryProposal is not null;
    public bool CanPrepareDraft => IsSafe && HasDraft;
    public bool IsManualReview => !IsSafe;
    public bool IsNoAction => IsSafe && !HasDraft;
    public string RouteLabel => RouteHint.ToString();
    public string ActionLabel => AuthorityConfirmation is not null
        ? "Generate Authority Confirmation draft"
        : LibraryProposal is not null
            ? "Generate Library Proposal draft"
            : IsManualReview
                ? "Manual review required"
                : "No governance draft";
}

public sealed record LeaderGovernanceDraftPreview(
    LeaderGovernanceRouteSuggestion Suggestion,
    AuthorityConfirmationDraft? AuthorityConfirmation,
    LeaderLibraryProposalDraftSuggestion? LibraryProposal)
{
    public bool IsAuthorityConfirmation => AuthorityConfirmation is not null;
    public bool IsLibraryProposal => LibraryProposal is not null;
    public string Title => IsAuthorityConfirmation
        ? AuthorityConfirmation!.Title
        : LibraryProposal!.Topic;
    public string Body => IsAuthorityConfirmation
        ? string.Join("\n", AuthorityConfirmation!.Statements)
        : LibraryProposal!.NodeContent;
}

public static class LeaderGovernanceRouteSuggestionBuilder
{
    public static LeaderGovernanceRouteSuggestion Create(Guid projectId, LeaderEvolutionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var expectedRoute = candidate.ImpactClass switch
        {
            LeaderEvolutionImpactClass.WorldRule => LeaderEvolutionRouteHint.AuthorityConfirmation,
            LeaderEvolutionImpactClass.CharacterOrObject or LeaderEvolutionImpactClass.Content => LeaderEvolutionRouteHint.LibraryProposal,
            LeaderEvolutionImpactClass.ProjectStructure or LeaderEvolutionImpactClass.Architecture or LeaderEvolutionImpactClass.Unclassified => LeaderEvolutionRouteHint.Unclassified,
            _ => LeaderEvolutionRouteHint.Unclassified
        };

        if (expectedRoute == LeaderEvolutionRouteHint.Unclassified)
        {
            return new(candidate, LeaderEvolutionRouteHint.Unclassified, candidate.RouteHint == LeaderEvolutionRouteHint.Unclassified, "No automatic governance route is defined for this impact class.", null, null);
        }

        if (candidate.RouteHint != expectedRoute)
        {
            return new(candidate, candidate.RouteHint, false, $"The suggested route conflicts with the safe route for {candidate.ImpactClass}.", null, null);
        }

        return expectedRoute switch
        {
            LeaderEvolutionRouteHint.AuthorityConfirmation => CreateAuthority(projectId, candidate),
            LeaderEvolutionRouteHint.LibraryProposal => CreateLibrary(candidate),
            _ => new(candidate, candidate.RouteHint, false, "The suggested route is not supported by this experiment.", null, null)
        };
    }

    private static LeaderGovernanceRouteSuggestion CreateAuthority(Guid projectId, LeaderEvolutionCandidate candidate)
    {
        var statement = BuildChangeStatement(candidate);
        var draft = new AuthorityConfirmationDraft(
            projectId,
            $"Confirm {candidate.Object} project rule change",
            [new AcceptedContributionInstruction(statement, new ContributionScopeTarget.Project(new ProjectRef(projectId)), null, null)]);
        return new(candidate, LeaderEvolutionRouteHint.AuthorityConfirmation, true, "This is an ephemeral draft. User acceptance must still use the existing Authority path.", draft, null);
    }

    private static LeaderGovernanceRouteSuggestion CreateLibrary(LeaderEvolutionCandidate candidate)
    {
        var draft = new LeaderLibraryProposalDraftSuggestion(
            candidate.ImpactClass.ToString(),
            candidate.Object,
            BuildChangeStatement(candidate));
        return new(candidate, LeaderEvolutionRouteHint.LibraryProposal, true, "This is an ephemeral draft. It has not been submitted to the Library.", null, draft);
    }

    private static string BuildChangeStatement(LeaderEvolutionCandidate candidate) =>
        candidate.Before is not null && candidate.After is not null
            ? $"{candidate.Object}: {candidate.Before} → {candidate.After}"
            : candidate.After is not null
                ? $"{candidate.Object}: {candidate.After}"
                : $"{candidate.Object}: {candidate.ChangeType}";
}
