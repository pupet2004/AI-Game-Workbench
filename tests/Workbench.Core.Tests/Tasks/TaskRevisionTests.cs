using Workbench.Core.Tasks;
using Workbench.Core.Workers;

namespace Workbench.Core.Tests.Tasks;

public sealed class TaskRevisionTests
{
    [Fact]
    public void Draft_contains_complete_contract_without_execution_identity()
    {
        var revision = CreateRevision();
        var draft = new TaskDraft(
            revision.TaskId,
            "Add feature",
            "Build the feature",
            "src/",
            "No migrations",
            ["Build passes"],
            TaskRiskLevel.Medium,
            ExecutionProfile.Create("provider", "account", "model", "runtime"),
            DateTimeOffset.UtcNow,
            revision);

        Assert.Equal(revision.TaskId, draft.TaskId);
        Assert.Equal("Add feature", draft.Title);
        Assert.Equal("Build the feature", draft.Goal);
        Assert.Equal("src/", draft.Scope);
        Assert.Equal("No migrations", draft.OutOfScope);
        Assert.Equal(["Build passes"], draft.AcceptanceCriteria);
        Assert.Equal(TaskRiskLevel.Medium, draft.RiskLevel);
        Assert.Equal(TaskLifecycleStatus.Draft, draft.Status);
        Assert.Equal(revision, draft.CurrentRevision);
        Assert.Null(draft.ExecutionIdentity);
    }

    [Fact]
    public void Draft_ready_to_start_is_domain_state_only_and_cancel_does_not_create_identity()
    {
        var draft = CreateDraft();

        var ready = draft.Edit(status: TaskLifecycleStatus.ReadyToStart);
        var cancelled = ready.Cancel();

        Assert.Equal(TaskLifecycleStatus.ReadyToStart, ready.Status);
        Assert.Equal(TaskLifecycleStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.ExecutionIdentity);
    }

    [Fact]
    public void Task_revision_is_an_immutable_full_snapshot_and_previous_revision_remains_unchanged()
    {
        var first = CreateRevision();
        var second = first.CreateSuccessor(
            goal: "Changed goal",
            changeReason: "User approved scope change",
            approvedBy: TaskRevisionApprover.User);

        Assert.Equal(1, first.RevisionNumber);
        Assert.Equal("Build the feature", first.Goal);
        Assert.Equal(2, second.RevisionNumber);
        Assert.Equal(first.Id, second.PreviousRevisionId);
        Assert.Equal("Changed goal", second.Goal);
        Assert.Equal(TaskRevisionApprover.User, second.ApprovedBy);
    }

    [Fact]
    public void Contract_acceptance_lists_are_not_mutable_through_the_public_view()
    {
        var criteria = new List<string> { "Build passes" };
        var revision = new TaskRevision(
            Guid.NewGuid(), 1, "Goal", "Scope", "Out", criteria, TaskRiskLevel.Low,
            ExecutionProfile.Create("provider", "account", "model", "runtime"),
            "Initial", TaskRevisionApprover.User, DateTimeOffset.UtcNow, null);

        criteria[0] = "Changed externally";

        Assert.Equal("Build passes", revision.Acceptance[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)revision.Acceptance)[0] = "Changed through view");
    }

    private static TaskDraft CreateDraft() =>
        CreateDraftWithId(Guid.NewGuid());

    private static TaskDraft CreateDraftWithId(Guid taskId) =>
        new(
            taskId,
            "Add feature",
            "Build the feature",
            "src/",
            "No migrations",
            ["Build passes"],
            TaskRiskLevel.Medium,
            ExecutionProfile.Create("provider", "account", "model", "runtime"),
            DateTimeOffset.UtcNow,
            CreateRevision(taskId));

    private static TaskRevision CreateRevision(Guid? taskId = null) =>
        new(
            taskId ?? Guid.NewGuid(),
            1,
            "Build the feature",
            "src/",
            "No migrations",
            ["Build passes"],
            TaskRiskLevel.Medium,
            ExecutionProfile.Create("provider", "account", "model", "runtime"),
            "Initial contract",
            TaskRevisionApprover.User,
            DateTimeOffset.UtcNow,
            null);
}
