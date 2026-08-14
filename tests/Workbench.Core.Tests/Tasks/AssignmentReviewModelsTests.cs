using Workbench.Core.Tasks;

namespace Workbench.Core.Tests.Tasks;

public sealed class AssignmentReviewModelsTests
{
    [Fact]
    public void Assignment_identity_is_task_id_and_review_outcome_is_not_an_action_level()
    {
        var assignmentId = Guid.NewGuid();
        var workerSessionId = Guid.NewGuid();
        var identity = new AssignmentReviewIdentity(assignmentId, Guid.NewGuid(), workerSessionId);

        Assert.Equal(assignmentId, identity.AssignmentId);
        Assert.Equal(assignmentId, identity.TaskId);
        Assert.NotEqual(identity.AssignmentId, identity.WorkerSessionId);
        Assert.NotEqual(LeaderReviewOutcome.Pass.ToString(), LeaderReviewActionLevel.L1LocalFix.ToString());
    }

    [Theory]
    [InlineData(TaskLifecycleStatus.Draft, TaskLifecycleStatus.ReadyToStart)]
    [InlineData(TaskLifecycleStatus.ReadyToStart, TaskLifecycleStatus.Working)]
    [InlineData(TaskLifecycleStatus.Working, TaskLifecycleStatus.Reviewing)]
    [InlineData(TaskLifecycleStatus.Working, TaskLifecycleStatus.NeedsLeaderDecision)]
    [InlineData(TaskLifecycleStatus.NeedsLeaderDecision, TaskLifecycleStatus.Working)]
    [InlineData(TaskLifecycleStatus.Reviewing, TaskLifecycleStatus.Working)]
    [InlineData(TaskLifecycleStatus.Reviewing, TaskLifecycleStatus.NeedsUserDecision)]
    [InlineData(TaskLifecycleStatus.NeedsUserDecision, TaskLifecycleStatus.Reviewing)]
    [InlineData(TaskLifecycleStatus.Reviewing, TaskLifecycleStatus.Completed)]
    public void Lifecycle_accepts_only_the_required_non_terminal_edges(TaskLifecycleStatus current, TaskLifecycleStatus next)
    {
        Assert.True(AssignmentLifecycle.CanTransition(current, next));
    }

    [Fact]
    public void Completed_is_terminal_and_does_not_express_worker_removal()
    {
        Assert.False(AssignmentLifecycle.CanTransition(TaskLifecycleStatus.Completed, TaskLifecycleStatus.Working));
        Assert.False(AssignmentLifecycle.CanTransition(TaskLifecycleStatus.Completed, TaskLifecycleStatus.Cancelled));
    }

    [Theory]
    [InlineData(TaskLifecycleStatus.Draft, TaskLifecycleStatus.Working)]
    [InlineData(TaskLifecycleStatus.ReadyToStart, TaskLifecycleStatus.Reviewing)]
    [InlineData(TaskLifecycleStatus.Working, TaskLifecycleStatus.Completed)]
    [InlineData(TaskLifecycleStatus.Reviewing, TaskLifecycleStatus.NeedsLeaderDecision)]
    [InlineData(TaskLifecycleStatus.NeedsUserDecision, TaskLifecycleStatus.Working)]
    [InlineData(TaskLifecycleStatus.Cancelled, TaskLifecycleStatus.Working)]
    public void Lifecycle_rejects_skipped_or_terminal_edges(TaskLifecycleStatus current, TaskLifecycleStatus next)
    {
        Assert.False(AssignmentLifecycle.CanTransition(current, next));
    }
}
