using Workbench.Core.Tasks;

namespace Workbench.Core.Leaders;

public enum LeaderAuthorityResolution
{
    AutoProceed,
    NotifyAndProceed,
    AskUser
}

public static class LeaderAuthorityResolver
{
    public static LeaderAuthorityResolution Resolve(
        LeaderAuthorityMode authority,
        LeaderReviewActionLevel actionLevel,
        LeaderReviewOutcome outcome)
    {
        if (!Enum.IsDefined(authority)) throw new ArgumentOutOfRangeException(nameof(authority));
        if (!Enum.IsDefined(actionLevel)) throw new ArgumentOutOfRangeException(nameof(actionLevel));
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));

        if (outcome == LeaderReviewOutcome.AskUser || actionLevel == LeaderReviewActionLevel.L3DecisionRequired)
        {
            return LeaderAuthorityResolution.AskUser;
        }

        return (authority, actionLevel) switch
        {
            (_, LeaderReviewActionLevel.L1LocalFix) => LeaderAuthorityResolution.AutoProceed,
            (LeaderAuthorityMode.Cautious, LeaderReviewActionLevel.L2TaskRework) => LeaderAuthorityResolution.AskUser,
            (LeaderAuthorityMode.Balanced, LeaderReviewActionLevel.L2TaskRework) => LeaderAuthorityResolution.NotifyAndProceed,
            (LeaderAuthorityMode.Autonomous, LeaderReviewActionLevel.L2TaskRework) => LeaderAuthorityResolution.AutoProceed,
            _ => throw new ArgumentOutOfRangeException(nameof(actionLevel))
        };
    }
}
