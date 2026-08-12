namespace Workbench.Core.Leaders;

public static class LeaderSessionRotationPolicyResolver
{
    public static LeaderSessionRotationPolicy Resolve(
        LeaderSessionRotationPolicy globalPolicy,
        LeaderSessionRotationPolicy? projectOverride) =>
        projectOverride ?? globalPolicy;
}
