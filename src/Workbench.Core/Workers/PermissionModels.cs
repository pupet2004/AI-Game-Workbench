namespace Workbench.Core.Workers;

public enum PermissionAccessMode
{
    Read,
    Write,
    Execute
}

public enum PermissionRisk
{
    Low,
    Medium,
    High
}

public enum WorkerCapability
{
    Merge,
    Push,
    MainWorktreeWrite,
    ProjectExternalWrite,
    ProjectExternalDelete,
    Credentials,
    SystemSettings,
    GlobalInstall,
    IrreversibleDestructiveAction,
    MaterialContractChange,
    ReadProject,
    AssignedWorktreeWrite,
    NormalBuildTest,
    LocalGitCommit
}

public sealed record PermissionRequest
{
    public PermissionRequest(
        Guid taskId,
        int revisionNumber,
        string requestedCapability,
        string scope,
        PermissionAccessMode accessMode,
        string reason,
        PermissionRisk risk,
        TimeSpan requestedDuration,
        DateTimeOffset createdAt)
    {
        if (taskId == Guid.Empty) throw new ArgumentException("TaskId is required.", nameof(taskId));
        if (revisionNumber < 1) throw new ArgumentOutOfRangeException(nameof(revisionNumber));
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedCapability);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (requestedDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requestedDuration));
        TaskId = taskId;
        RevisionNumber = revisionNumber;
        RequestedCapability = requestedCapability;
        Scope = scope;
        AccessMode = accessMode;
        Reason = reason;
        Risk = risk;
        RequestedDuration = requestedDuration;
        CreatedAt = createdAt;
    }

    public Guid TaskId { get; }
    public int RevisionNumber { get; }
    public string RequestedCapability { get; }
    public string Scope { get; }
    public PermissionAccessMode AccessMode { get; }
    public string Reason { get; }
    public PermissionRisk Risk { get; }
    public TimeSpan RequestedDuration { get; }
    public DateTimeOffset CreatedAt { get; }
}

public sealed record TaskGrant
{
    public TaskGrant(Guid taskId, string capability, string scope, DateTimeOffset expiry)
    {
        if (taskId == Guid.Empty) throw new ArgumentException("TaskId is required.", nameof(taskId));
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        TaskId = taskId;
        Capability = capability;
        Scope = scope;
        Expiry = expiry;
    }

    public Guid TaskId { get; }
    public string Capability { get; }
    public string Scope { get; }
    public DateTimeOffset Expiry { get; }
    public bool IsProjectGlobal => false;
}
