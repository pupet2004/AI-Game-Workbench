using Workbench.Core.Workers;

namespace Workbench.Core.Tests.Workers;

public sealed class PermissionModelTests
{
    [Fact]
    public void Permission_request_contains_task_revision_capability_scope_and_risk()
    {
        var taskId = Guid.NewGuid();
        var request = new PermissionRequest(
            taskId,
            2,
            "install-local-tool",
            "assigned worktree",
            PermissionAccessMode.Execute,
            "Build requires the local tool",
            PermissionRisk.Low,
            TimeSpan.FromMinutes(10),
            DateTimeOffset.UtcNow);

        Assert.Equal(taskId, request.TaskId);
        Assert.Equal(2, request.RevisionNumber);
        Assert.Equal("install-local-tool", request.RequestedCapability);
        Assert.Equal("assigned worktree", request.Scope);
    }

    [Fact]
    public void Grant_is_task_scoped_and_hard_boundaries_are_representable()
    {
        var taskId = Guid.NewGuid();
        var grant = new TaskGrant(taskId, "read-project", "assigned worktree", DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.Equal(taskId, grant.TaskId);
        Assert.False(grant.IsProjectGlobal);
        Assert.Contains(WorkerCapability.Merge, Enum.GetValues<WorkerCapability>());
        Assert.Contains(WorkerCapability.Push, Enum.GetValues<WorkerCapability>());
        Assert.Contains(WorkerCapability.MainWorktreeWrite, Enum.GetValues<WorkerCapability>());
        Assert.Contains(WorkerCapability.ProjectExternalWrite, Enum.GetValues<WorkerCapability>());
        Assert.Contains(WorkerCapability.Credentials, Enum.GetValues<WorkerCapability>());
        Assert.Contains(WorkerCapability.SystemSettings, Enum.GetValues<WorkerCapability>());
        Assert.Contains(WorkerCapability.GlobalInstall, Enum.GetValues<WorkerCapability>());
        Assert.Contains(WorkerCapability.IrreversibleDestructiveAction, Enum.GetValues<WorkerCapability>());
        Assert.Contains(WorkerCapability.MaterialContractChange, Enum.GetValues<WorkerCapability>());
    }

    [Fact]
    public void Clarification_request_is_distinct_from_permission_request()
    {
        var request = new TaskClarificationRequest(
            Guid.NewGuid(),
            1,
            "Does this include the migration?",
            TaskClarificationCategory.ScopeConflict,
            DateTimeOffset.UtcNow);

        Assert.Equal(TaskClarificationCategory.ScopeConflict, request.Category);
        Assert.IsNotType<PermissionRequest>(request);
    }

    [Fact]
    public void Permission_and_clarification_requests_reject_invalid_identity()
    {
        Assert.Throws<ArgumentException>(() => new PermissionRequest(
            Guid.Empty, 0, "read", "scope", PermissionAccessMode.Read,
            "reason", PermissionRisk.Low, TimeSpan.FromMinutes(1), DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => new TaskClarificationRequest(
            Guid.Empty, 0, "question", TaskClarificationCategory.GoalAmbiguity, DateTimeOffset.UtcNow));
    }
}
