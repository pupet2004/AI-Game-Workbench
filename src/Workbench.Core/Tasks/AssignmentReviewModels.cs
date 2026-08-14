namespace Workbench.Core.Tasks;

public enum LeaderReviewOutcome
{
    Pass,
    Fix,
    Continue,
    AskUser
}

public enum LeaderReviewActionLevel
{
    L1LocalFix,
    L2TaskRework,
    L3DecisionRequired
}

public sealed record AssignmentReviewIdentity(Guid TaskId, Guid TaskRevisionId, Guid WorkerSessionId)
{
    public Guid AssignmentId => TaskId;
}

public static class AssignmentLifecycle
{
    public static bool CanTransition(TaskLifecycleStatus current, TaskLifecycleStatus next) =>
        (current, next) switch
        {
            (TaskLifecycleStatus.Draft, TaskLifecycleStatus.ReadyToStart) => true,
            (TaskLifecycleStatus.ReadyToStart, TaskLifecycleStatus.Working) => true,
            (TaskLifecycleStatus.Working, TaskLifecycleStatus.NeedsLeaderDecision) => true,
            (TaskLifecycleStatus.NeedsLeaderDecision, TaskLifecycleStatus.Working) => true,
            (TaskLifecycleStatus.Working, TaskLifecycleStatus.Reviewing) => true,
            (TaskLifecycleStatus.Reviewing, TaskLifecycleStatus.Working) => true,
            (TaskLifecycleStatus.Reviewing, TaskLifecycleStatus.NeedsUserDecision) => true,
            (TaskLifecycleStatus.NeedsUserDecision, TaskLifecycleStatus.Reviewing) => true,
            (TaskLifecycleStatus.Reviewing, TaskLifecycleStatus.Completed) => true,
            (_, TaskLifecycleStatus.Cancelled) when current != TaskLifecycleStatus.Cancelled && current != TaskLifecycleStatus.Completed => true,
            _ => false
        };
}
