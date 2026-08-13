using Workbench.Core.Tasks;
using Workbench.Core.Workers;

namespace Workbench.Core.Tests.Workers;

public sealed class WorkerExecutionIdentityTests
{
    [Fact]
    public void Start_revision_is_immutable_and_ack_advances_only_current_revision()
    {
        var taskId = Guid.NewGuid();
        var start = CreateRevision(taskId, 1, "Initial");
        var next = CreateRevision(taskId, 2, "Approved change");
        var identity = CreateIdentity(start);

        var acknowledged = identity.Acknowledge(next.CreateReference(), new TaskRevisionAck(identity.TaskId, next.Id, next.RevisionNumber));

        Assert.Equal(start.Id, acknowledged.ExecutionStartRevision.RevisionId);
        Assert.Equal(next.Id, acknowledged.CurrentAcknowledgedRevision.RevisionId);
        Assert.Equal(identity.BaseCommit, acknowledged.BaseCommit);
        Assert.Equal(identity.TargetBranch, acknowledged.TargetBranch);
        Assert.Equal(identity.ProviderAccount, acknowledged.ProviderAccount);
        Assert.Equal(identity.ExecutionProfile, acknowledged.ExecutionProfile);
        Assert.Equal(identity.WorkerBranch, acknowledged.WorkerBranch);
        Assert.Equal(identity.WorkerWorktreePath, acknowledged.WorkerWorktreePath);
    }

    [Fact]
    public void Stale_wrong_task_wrong_revision_and_duplicate_ack_are_rejected_deterministically()
    {
        var taskId = Guid.NewGuid();
        var start = CreateRevision(taskId, 1, "Initial");
        var next = CreateRevision(taskId, 2, "Approved change");
        var identity = CreateIdentity(start);
        var ack = new TaskRevisionAck(identity.TaskId, next.Id, next.RevisionNumber);
        var advanced = identity.Acknowledge(next.CreateReference(), ack);

        var staleAck = new TaskRevisionAck(identity.TaskId, start.Id, start.RevisionNumber);
        Assert.Throws<InvalidOperationException>(() => advanced.Acknowledge(start.CreateReference(), staleAck));
        Assert.Throws<ArgumentException>(() => advanced.Acknowledge(next.CreateReference(), new TaskRevisionAck(Guid.NewGuid(), next.Id, next.RevisionNumber)));
        Assert.Throws<ArgumentException>(() => advanced.Acknowledge(new TaskRevisionReference(identity.TaskId, Guid.NewGuid(), 3), ack));
        Assert.Same(advanced, advanced.Acknowledge(next.CreateReference(), ack));
    }

    [Fact]
    public void Execution_state_contract_contains_only_approved_states()
    {
        Assert.Equal(
            ["Preparing", "WorkspaceCreating", "WorkspaceCreated", "RuntimeStarting", "Running", "Blocked", "Interrupted", "CompletedPendingReview", "Failed"],
            Enum.GetNames<WorkerExecutionState>());
    }

    [Fact]
    public void Leader_recommendation_does_not_authorize_terminal_failed()
    {
        var recommendation = TerminationRecommendation.Leader("Provider cannot continue");

        Assert.False(recommendation.AuthorizesTerminalFailure);
        Assert.Throws<InvalidOperationException>(() => WorkerExecutionFailure.Authorize(recommendation, userDecision: null));
        Assert.True(WorkerExecutionFailure.Authorize(recommendation, new UserTerminationDecision("Abandon current contract")).IsTerminal);
    }

    private static WorkerExecutionIdentity CreateIdentity(TaskRevision revision) =>
        WorkerExecutionIdentity.Start(
            revision.CreateReference(),
            "0123456789012345678901234567890123456789",
            "master",
            ProviderAccountBinding.Create("provider", "account"),
            revision.RecommendedExecutionProfile,
            "worker/task-1",
            "C:/worker/task-1");

    private static TaskRevision CreateRevision(Guid taskId, int number, string goal) =>
        new(
            taskId,
            number,
            goal,
            "src/",
            "No migrations",
            ["Build passes"],
            TaskRiskLevel.Medium,
            ExecutionProfile.Create("provider", "account", "model", "runtime"),
            "Approved",
            TaskRevisionApprover.User,
            DateTimeOffset.UtcNow,
            null);
}
