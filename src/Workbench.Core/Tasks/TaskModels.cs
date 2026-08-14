using Workbench.Core.Workers;

namespace Workbench.Core.Tasks;

public enum TaskLifecycleStatus
{
    Draft,
    ReadyToStart,
    Working,
    NeedsLeaderDecision,
    Reviewing,
    NeedsUserDecision,
    Completed,
    Cancelled
}

public enum TaskRiskLevel
{
    Low,
    Medium,
    High
}

public enum TaskRevisionApprover
{
    User
}

public sealed record ExecutionProfile
{
    private ExecutionProfile(string providerId, string providerAccountId, string modelProfileId, string agentRuntimeId)
    {
        ProviderId = providerId;
        ProviderAccountId = providerAccountId;
        ModelProfileId = modelProfileId;
        AgentRuntimeId = agentRuntimeId;
    }

    public string ProviderId { get; }

    public string ProviderAccountId { get; }

    public string ModelProfileId { get; }

    public string AgentRuntimeId { get; }

    public static ExecutionProfile Create(
        string providerId,
        string providerAccountId,
        string modelProfileId,
        string agentRuntimeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerAccountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentRuntimeId);
        return new ExecutionProfile(providerId, providerAccountId, modelProfileId, agentRuntimeId);
    }
}

public sealed record TaskRevision
{
    public TaskRevision(
        Guid taskId,
        int revisionNumber,
        string goal,
        string scope,
        string outOfScope,
        IReadOnlyList<string> acceptance,
        TaskRiskLevel riskLevel,
        ExecutionProfile recommendedExecutionProfile,
        string changeReason,
        TaskRevisionApprover approvedBy,
        DateTimeOffset createdAt,
        Guid? previousRevisionId)
    {
        if (taskId == Guid.Empty) throw new ArgumentException("TaskId is required.", nameof(taskId));
        if (revisionNumber < 1) throw new ArgumentOutOfRangeException(nameof(revisionNumber));
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(outOfScope);
        ArgumentNullException.ThrowIfNull(acceptance);
        if (acceptance.Count == 0 || acceptance.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one acceptance criterion is required.", nameof(acceptance));
        }

        ArgumentNullException.ThrowIfNull(recommendedExecutionProfile);
        ArgumentException.ThrowIfNullOrWhiteSpace(changeReason);
        TaskId = taskId;
        RevisionNumber = revisionNumber;
        Goal = goal;
        Scope = scope;
        OutOfScope = outOfScope;
        Acceptance = Array.AsReadOnly(acceptance.ToArray());
        RiskLevel = riskLevel;
        RecommendedExecutionProfile = recommendedExecutionProfile;
        ChangeReason = changeReason;
        ApprovedBy = approvedBy;
        CreatedAt = createdAt;
        PreviousRevisionId = previousRevisionId;
        Id = Guid.NewGuid();
    }

    public Guid Id { get; }

    public Guid TaskId { get; }

    public int RevisionNumber { get; }

    public string Goal { get; }

    public string Scope { get; }

    public string OutOfScope { get; }

    public IReadOnlyList<string> Acceptance { get; }

    public TaskRiskLevel RiskLevel { get; }

    public ExecutionProfile RecommendedExecutionProfile { get; }

    public string ChangeReason { get; }

    public TaskRevisionApprover ApprovedBy { get; }

    public DateTimeOffset CreatedAt { get; }

    public Guid? PreviousRevisionId { get; }

    public TaskRevisionReference CreateReference() => new(TaskId, Id, RevisionNumber);

    public TaskRevision CreateSuccessor(string goal, string changeReason, TaskRevisionApprover approvedBy) =>
        new(TaskId, RevisionNumber + 1, goal, Scope, OutOfScope, Acceptance, RiskLevel,
            RecommendedExecutionProfile, changeReason, approvedBy, DateTimeOffset.UtcNow, Id);
}

public sealed record TaskDraft
{
    public TaskDraft(
        Guid taskId,
        string title,
        string goal,
        string scope,
        string outOfScope,
        IReadOnlyList<string> acceptanceCriteria,
        TaskRiskLevel riskLevel,
        ExecutionProfile recommendedExecutionProfile,
        DateTimeOffset createdAt,
        TaskRevision currentRevision,
        TaskLifecycleStatus status = TaskLifecycleStatus.Draft,
        object? executionIdentity = null)
    {
        if (taskId == Guid.Empty) throw new ArgumentException("TaskId is required.", nameof(taskId));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(outOfScope);
        ArgumentNullException.ThrowIfNull(acceptanceCriteria);
        ArgumentNullException.ThrowIfNull(recommendedExecutionProfile);
        ArgumentNullException.ThrowIfNull(currentRevision);
        if (currentRevision.TaskId != taskId)
        {
            throw new ArgumentException("Current revision must belong to the task.", nameof(currentRevision));
        }

        TaskId = taskId;
        Title = title;
        Goal = goal;
        Scope = scope;
        OutOfScope = outOfScope;
        AcceptanceCriteria = Array.AsReadOnly(acceptanceCriteria.ToArray());
        RiskLevel = riskLevel;
        RecommendedExecutionProfile = recommendedExecutionProfile;
        CreatedAt = createdAt;
        CurrentRevision = currentRevision;
        Status = status;
        ExecutionIdentity = executionIdentity;
    }

    public Guid TaskId { get; }
    public string Title { get; }
    public string Goal { get; }
    public string Scope { get; }
    public string OutOfScope { get; }
    public IReadOnlyList<string> AcceptanceCriteria { get; }
    public TaskRiskLevel RiskLevel { get; }
    public ExecutionProfile RecommendedExecutionProfile { get; }
    public DateTimeOffset CreatedAt { get; }
    public TaskRevision CurrentRevision { get; }
    public TaskLifecycleStatus Status { get; init; }
    public object? ExecutionIdentity { get; init; }

    public TaskDraft Edit(TaskLifecycleStatus? status = null) =>
        this with { Status = status ?? Status };

    public TaskDraft Cancel() => this with { Status = TaskLifecycleStatus.Cancelled, ExecutionIdentity = null };
}
