namespace Workbench.Runtime.Agents;

public abstract record AgentEvent(DateTimeOffset OccurredAt);

public sealed record AgentTextDelta(
    string Text,
    DateTimeOffset OccurredAt) : AgentEvent(OccurredAt);

public enum AgentMessageRole
{
    User,
    Assistant
}

public sealed record AgentMessage(
    AgentMessageRole Role,
    string Text,
    DateTimeOffset OccurredAt) : AgentEvent(OccurredAt);

public sealed record AgentStatusChanged(
    AgentSessionStatus Status,
    DateTimeOffset OccurredAt) : AgentEvent(OccurredAt);

public sealed record AgentApprovalRequested : AgentEvent
{
    public AgentApprovalRequested(
        AgentApprovalRequestId requestId,
        AgentSessionId sessionId,
        string summary,
        IReadOnlyList<AgentApprovalOption> options,
        DateTimeOffset occurredAt)
        : base(occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Count == 0)
        {
            throw new ArgumentException("At least one approval option is required.", nameof(options));
        }

        RequestId = requestId;
        SessionId = sessionId;
        Summary = summary;
        Options = options.ToArray();
    }

    public AgentApprovalRequestId RequestId { get; }

    public AgentSessionId SessionId { get; }

    public string Summary { get; }

    public IReadOnlyList<AgentApprovalOption> Options { get; }
}

public sealed record AgentToolEvent(
    string ToolName,
    string? Detail,
    DateTimeOffset OccurredAt,
    bool IsCompleted = false) : AgentEvent(OccurredAt);

public sealed record AgentQuestionOption(string Id, string Label, string? Description = null);

public sealed record AgentQuestionRequested(
    string RequestId,
    AgentSessionId SessionId,
    string Prompt,
    IReadOnlyList<AgentQuestionOption> Options,
    DateTimeOffset OccurredAt) : AgentEvent(OccurredAt);

public sealed record AgentError(
    string Message,
    DateTimeOffset OccurredAt) : AgentEvent(OccurredAt);

public sealed record AgentTurnCompleted(
    AgentResult Result,
    DateTimeOffset OccurredAt) : AgentEvent(OccurredAt);

public sealed record AgentProgressChanged(
    int CompletedSteps,
    DateTimeOffset OccurredAt) : AgentEvent(OccurredAt);

public enum AgentPlanStepStatus
{
    Pending,
    InProgress,
    Completed,
    Failed
}

public sealed record AgentPlanStep(
    string Id,
    string Text,
    AgentPlanStepStatus Status);

public sealed record AgentPlanUpdated(
    IReadOnlyList<AgentPlanStep> Steps,
    DateTimeOffset OccurredAt) : AgentEvent(OccurredAt);
