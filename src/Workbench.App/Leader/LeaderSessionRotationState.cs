using Workbench.Core.Leaders;

namespace Workbench.App.Leader;

public sealed record LeaderSessionRotationState(
    LeaderSessionRotationPolicy EffectivePolicy,
    LeaderSessionRotationPolicy? ProjectOverride,
    LeaderSessionRotationEvaluation Evaluation);
