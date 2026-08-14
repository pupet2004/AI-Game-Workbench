namespace Workbench.Core.Leaders;

public enum LeaderAuthorityMode
{
    Cautious,
    Balanced,
    Autonomous
}

public static class LeaderAuthorityModeResolver
{
    public static LeaderAuthorityMode Resolve(LeaderAuthorityMode globalDefault, LeaderAuthorityMode? projectOverride) =>
        projectOverride ?? globalDefault;
}
