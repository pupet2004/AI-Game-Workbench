using Workbench.App.Leader;
using Workbench.Core.Continuity;

namespace Workbench.App.Tests;

public sealed class LeaderGovernanceRoutingTests
{
    [Fact]
    public void World_rule_candidate_creates_only_an_ephemeral_authority_draft()
    {
        var projectId = Guid.NewGuid();
        var candidate = Candidate(LeaderEvolutionImpactClass.WorldRule, LeaderEvolutionRouteHint.AuthorityConfirmation);

        var suggestion = LeaderGovernanceRouteSuggestionBuilder.Create(projectId, candidate);

        Assert.True(suggestion.IsSafe);
        Assert.True(suggestion.CanPrepareDraft);
        Assert.NotNull(suggestion.AuthorityConfirmation);
        Assert.Null(suggestion.LibraryProposal);
        Assert.Equal(projectId, suggestion.AuthorityConfirmation!.ProjectId);
        Assert.Contains("无限计算器", suggestion.AuthorityConfirmation.Statements.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void Content_candidate_creates_only_an_ephemeral_library_draft()
    {
        var suggestion = LeaderGovernanceRouteSuggestionBuilder.Create(
            Guid.NewGuid(),
            Candidate(LeaderEvolutionImpactClass.Content, LeaderEvolutionRouteHint.LibraryProposal));

        Assert.True(suggestion.IsSafe);
        Assert.True(suggestion.CanPrepareDraft);
        Assert.Null(suggestion.AuthorityConfirmation);
        Assert.NotNull(suggestion.LibraryProposal);
        Assert.Equal("Content", suggestion.LibraryProposal!.Category);
    }

    [Fact]
    public void Conflicting_route_is_manual_review_and_creates_no_draft()
    {
        var suggestion = LeaderGovernanceRouteSuggestionBuilder.Create(
            Guid.NewGuid(),
            Candidate(LeaderEvolutionImpactClass.WorldRule, LeaderEvolutionRouteHint.LibraryProposal));

        Assert.False(suggestion.IsSafe);
        Assert.True(suggestion.IsManualReview);
        Assert.False(suggestion.CanPrepareDraft);
        Assert.Null(suggestion.AuthorityConfirmation);
        Assert.Null(suggestion.LibraryProposal);
    }

    [Fact]
    public void Architecture_and_unclassified_candidates_remain_without_automatic_governance()
    {
        var architecture = LeaderGovernanceRouteSuggestionBuilder.Create(
            Guid.NewGuid(),
            Candidate(LeaderEvolutionImpactClass.Architecture, LeaderEvolutionRouteHint.Unclassified));
        var unclassified = LeaderGovernanceRouteSuggestionBuilder.Create(
            Guid.NewGuid(),
            Candidate(LeaderEvolutionImpactClass.Unclassified, LeaderEvolutionRouteHint.Unclassified));

        Assert.True(architecture.IsNoAction);
        Assert.True(unclassified.IsNoAction);
        Assert.Null(architecture.AuthorityConfirmation);
        Assert.Null(unclassified.LibraryProposal);
    }

    private static LeaderEvolutionCandidate Candidate(
        LeaderEvolutionImpactClass impactClass,
        LeaderEvolutionRouteHint routeHint) =>
        new(
            "无限计算器",
            "WorldRule",
            "ConstraintRevision",
            "可能控制人的时间行为",
            "只预测时空稳定性风险，不控制人的思想",
            impactClass,
            routeHint,
            "用户明确改变了项目设定",
            "current_user_message");
}
