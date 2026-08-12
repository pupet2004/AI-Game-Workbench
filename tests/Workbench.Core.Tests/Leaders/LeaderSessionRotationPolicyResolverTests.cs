using Workbench.Core.Leaders;

namespace Workbench.Core.Tests.Leaders;

public sealed class LeaderSessionRotationPolicyResolverTests
{
    [Theory]
    [InlineData(LeaderSessionRotationPolicy.Auto)]
    [InlineData(LeaderSessionRotationPolicy.Ask)]
    [InlineData(LeaderSessionRotationPolicy.ManualOnly)]
    public void No_override_uses_global_policy(LeaderSessionRotationPolicy globalPolicy)
    {
        Assert.Equal(globalPolicy, LeaderSessionRotationPolicyResolver.Resolve(globalPolicy, null));
    }

    [Theory]
    [InlineData(LeaderSessionRotationPolicy.ManualOnly, LeaderSessionRotationPolicy.Auto)]
    [InlineData(LeaderSessionRotationPolicy.Auto, LeaderSessionRotationPolicy.Ask)]
    [InlineData(LeaderSessionRotationPolicy.Ask, LeaderSessionRotationPolicy.ManualOnly)]
    public void Project_override_wins(LeaderSessionRotationPolicy globalPolicy, LeaderSessionRotationPolicy projectOverride)
    {
        Assert.Equal(projectOverride, LeaderSessionRotationPolicyResolver.Resolve(globalPolicy, projectOverride));
    }
}
