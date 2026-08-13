using Workbench.Core.Tasks;

namespace Workbench.Core.Workers;

public readonly record struct ProviderAccountBinding(string ProviderId, string AccountId)
{
    public static ProviderAccountBinding Create(string providerId, string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return new ProviderAccountBinding(providerId, accountId);
    }
}

public sealed record TaskRevisionReference(Guid TaskId, Guid RevisionId, int RevisionNumber);

public sealed record TaskRevisionAck(Guid TaskId, Guid RevisionId, int RevisionNumber);

public enum WorkerExecutionState
{
    Preparing,
    WorkspaceCreating,
    WorkspaceCreated,
    RuntimeStarting,
    Running,
    Blocked,
    Interrupted,
    CompletedPendingReview,
    Failed
}

public sealed record WorkerExecutionIdentity
{
    private WorkerExecutionIdentity(
        Guid taskId,
        TaskRevisionReference executionStartRevision,
        TaskRevisionReference currentAcknowledgedRevision,
        string baseCommit,
        string targetBranch,
        ProviderAccountBinding providerAccount,
        ExecutionProfile executionProfile,
        string workerBranch,
        string workerWorktreePath)
    {
        TaskId = taskId;
        ExecutionStartRevision = executionStartRevision;
        CurrentAcknowledgedRevision = currentAcknowledgedRevision;
        BaseCommit = baseCommit;
        TargetBranch = targetBranch;
        ProviderAccount = providerAccount;
        ExecutionProfile = executionProfile;
        WorkerBranch = workerBranch;
        WorkerWorktreePath = workerWorktreePath;
    }

    public Guid TaskId { get; }
    public TaskRevisionReference ExecutionStartRevision { get; }
    public TaskRevisionReference CurrentAcknowledgedRevision { get; init; }
    public string BaseCommit { get; }
    public string TargetBranch { get; }
    public ProviderAccountBinding ProviderAccount { get; }
    public ExecutionProfile ExecutionProfile { get; }
    public string WorkerBranch { get; }
    public string WorkerWorktreePath { get; }

    public static WorkerExecutionIdentity Start(
        TaskRevisionReference startRevision,
        string baseCommit,
        string targetBranch,
        ProviderAccountBinding providerAccount,
        ExecutionProfile executionProfile,
        string workerBranch,
        string workerWorktreePath)
    {
        if (startRevision.TaskId == Guid.Empty || startRevision.RevisionId == Guid.Empty || startRevision.RevisionNumber < 1)
        {
            throw new ArgumentException("A valid start revision is required.", nameof(startRevision));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(baseCommit);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetBranch);
        ArgumentNullException.ThrowIfNull(executionProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerWorktreePath);
        return new WorkerExecutionIdentity(startRevision.TaskId, startRevision, startRevision, baseCommit,
            targetBranch, providerAccount, executionProfile, workerBranch, workerWorktreePath);
    }

    public WorkerExecutionIdentity Acknowledge(TaskRevisionReference revision, TaskRevisionAck ack)
    {
        ArgumentNullException.ThrowIfNull(ack);
        if (ack.TaskId != TaskId || revision.TaskId != TaskId)
        {
            throw new ArgumentException("Revision acknowledgement belongs to another task.", nameof(ack));
        }

        if (ack.RevisionId != revision.RevisionId || ack.RevisionNumber != revision.RevisionNumber)
        {
            throw new ArgumentException("Revision acknowledgement does not match the revision.", nameof(ack));
        }

        if (revision.RevisionNumber < CurrentAcknowledgedRevision.RevisionNumber)
        {
            throw new InvalidOperationException("A stale revision acknowledgement cannot move execution backwards.");
        }

        if (revision == CurrentAcknowledgedRevision)
        {
            return this;
        }

        if (revision.RevisionNumber <= CurrentAcknowledgedRevision.RevisionNumber)
        {
            throw new InvalidOperationException("Revision acknowledgement must advance the current revision.");
        }

        return this with { CurrentAcknowledgedRevision = revision };
    }
}

public sealed record TerminationRecommendation(string Reason, bool AuthorizesTerminalFailure)
{
    public static TerminationRecommendation Leader(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new TerminationRecommendation(reason, false);
    }
}

public sealed record UserTerminationDecision
{
    public UserTerminationDecision(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    public string Reason { get; }
}

public sealed record WorkerExecutionFailure(bool IsTerminal, string Reason)
{
    public static WorkerExecutionFailure Authorize(TerminationRecommendation recommendation, UserTerminationDecision? userDecision)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        if (userDecision is null)
        {
            throw new InvalidOperationException("Terminal Failed requires an explicit user decision.");
        }

        return new WorkerExecutionFailure(true, userDecision.Reason);
    }
}
