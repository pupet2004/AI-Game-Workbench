using Workbench.Core.Leaders;

namespace Workbench.Core.Tests.Leaders;

public sealed class LeaderAuthorityModeResolverTests
{
    [Fact]
    public void Project_override_wins_and_null_inherits_global_default()
    {
        Assert.Equal(LeaderAuthorityMode.Balanced, LeaderAuthorityModeResolver.Resolve(LeaderAuthorityMode.Balanced, null));
        Assert.Equal(LeaderAuthorityMode.Autonomous, LeaderAuthorityModeResolver.Resolve(LeaderAuthorityMode.Cautious, LeaderAuthorityMode.Autonomous));
        Assert.Equal(LeaderAuthorityMode.Cautious, LeaderAuthorityModeResolver.Resolve(LeaderAuthorityMode.Cautious, null));
    }
}
