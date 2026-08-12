namespace Workbench.Runtime.Agents;

public abstract record AgentEvent(DateTimeOffset OccurredAt);

public sealed record AgentTextDelta(
    string Text,
    DateTimeOffset OccurredAt) : AgentEvent(OccurredAt);

public sealed record AgentStatusChanged(
    AgentSessionStatus Status,
    DateTimeOffset OccurredAt) : AgentEvent(OccurredAt);

public sealed record AgentApprovalRequested(
    string RequestId,
    string Description,
    DateTimeOffset OccurredAt) : AgentEvent(OccurredAt);

public sealed record AgentToolEvent(
    string ToolName,
    string? Detail,
    DateTimeOffset OccurredAt) : AgentEvent(OccurredAt);

public sealed record AgentError(
    string Message,
    DateTimeOffset OccurredAt) : AgentEvent(OccurredAt);
