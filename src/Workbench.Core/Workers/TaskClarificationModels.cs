namespace Workbench.Core.Workers;

public enum TaskClarificationCategory
{
    GoalAmbiguity,
    ScopeConflict,
    AcceptanceConflict,
    BaseCommitAssumptionConflict,
    ArchitectureProductDecisionRequired
}

public sealed record TaskClarificationRequest
{
    public TaskClarificationRequest(
        Guid taskId,
        int revisionNumber,
        string questionOrConflict,
        TaskClarificationCategory category,
        DateTimeOffset createdAt)
    {
        if (taskId == Guid.Empty) throw new ArgumentException("TaskId is required.", nameof(taskId));
        if (revisionNumber < 1) throw new ArgumentOutOfRangeException(nameof(revisionNumber));
        ArgumentException.ThrowIfNullOrWhiteSpace(questionOrConflict);
        TaskId = taskId;
        RevisionNumber = revisionNumber;
        QuestionOrConflict = questionOrConflict;
        Category = category;
        CreatedAt = createdAt;
    }

    public Guid TaskId { get; }
    public int RevisionNumber { get; }
    public string QuestionOrConflict { get; }
    public TaskClarificationCategory Category { get; }
    public DateTimeOffset CreatedAt { get; }
}
