using Workbench.Core.Leaders;
using Workbench.Core.Tasks;

namespace Workbench.Core.Tests.Leaders;

public sealed class LeaderAuthorityResolverTests
{
    [Theory]
    [InlineData(LeaderAuthorityMode.Cautious, LeaderReviewActionLevel.L1LocalFix, LeaderAuthorityResolution.AutoProceed)]
    [InlineData(LeaderAuthorityMode.Cautious, LeaderReviewActionLevel.L2TaskRework, LeaderAuthorityResolution.AskUser)]
    [InlineData(LeaderAuthorityMode.Cautious, LeaderReviewActionLevel.L3DecisionRequired, LeaderAuthorityResolution.AskUser)]
    [InlineData(LeaderAuthorityMode.Balanced, LeaderReviewActionLevel.L1LocalFix, LeaderAuthorityResolution.AutoProceed)]
    [InlineData(LeaderAuthorityMode.Balanced, LeaderReviewActionLevel.L2TaskRework, LeaderAuthorityResolution.NotifyAndProceed)]
    [InlineData(LeaderAuthorityMode.Balanced, LeaderReviewActionLevel.L3DecisionRequired, LeaderAuthorityResolution.AskUser)]
    [InlineData(LeaderAuthorityMode.Autonomous, LeaderReviewActionLevel.L1LocalFix, LeaderAuthorityResolution.AutoProceed)]
    [InlineData(LeaderAuthorityMode.Autonomous, LeaderReviewActionLevel.L2TaskRework, LeaderAuthorityResolution.AutoProceed)]
    [InlineData(LeaderAuthorityMode.Autonomous, LeaderReviewActionLevel.L3DecisionRequired, LeaderAuthorityResolution.AskUser)]
    public void Authority_matrix_is_resolved_from_action_level(LeaderAuthorityMode authority, LeaderReviewActionLevel actionLevel, LeaderAuthorityResolution expected)
    {
        Assert.Equal(expected, LeaderAuthorityResolver.Resolve(authority, actionLevel, LeaderReviewOutcome.Fix));
    }

    [Theory]
    [InlineData(LeaderAuthorityMode.Cautious)]
    [InlineData(LeaderAuthorityMode.Balanced)]
    [InlineData(LeaderAuthorityMode.Autonomous)]
    public void Ask_user_decisions_and_l3_always_require_the_user(LeaderAuthorityMode authority)
    {
        Assert.Equal(LeaderAuthorityResolution.AskUser, LeaderAuthorityResolver.Resolve(authority, LeaderReviewActionLevel.L3DecisionRequired, LeaderReviewOutcome.AskUser));
        Assert.Equal(LeaderAuthorityResolution.AskUser, LeaderAuthorityResolver.Resolve(authority, LeaderReviewActionLevel.L3DecisionRequired, LeaderReviewOutcome.Pass));
    }

    [Fact]
    public void Pass_does_not_bypass_action_level()
    {
        Assert.Equal(LeaderAuthorityResolution.AskUser, LeaderAuthorityResolver.Resolve(LeaderAuthorityMode.Cautious, LeaderReviewActionLevel.L2TaskRework, LeaderReviewOutcome.Pass));
    }

    [Fact]
    public void Invalid_enums_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LeaderAuthorityResolver.Resolve((LeaderAuthorityMode)99, LeaderReviewActionLevel.L1LocalFix, LeaderReviewOutcome.Pass));
        Assert.Throws<ArgumentOutOfRangeException>(() => LeaderAuthorityResolver.Resolve(LeaderAuthorityMode.Balanced, (LeaderReviewActionLevel)99, LeaderReviewOutcome.Pass));
        Assert.Throws<ArgumentOutOfRangeException>(() => LeaderAuthorityResolver.Resolve(LeaderAuthorityMode.Balanced, LeaderReviewActionLevel.L1LocalFix, (LeaderReviewOutcome)99));
    }
}
