namespace Workbench.Core.Leaders;

public sealed record LeaderSessionRotationEvaluation(
    LeaderSessionRotationPolicy EffectivePolicy,
    bool IsDue,
    LeaderSessionRotationReason? Reason);
