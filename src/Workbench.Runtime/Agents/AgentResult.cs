namespace Workbench.Runtime.Agents;

public sealed record AgentResult(
    AgentSessionId SessionId,
    AgentSessionStatus FinalStatus,
    string? FinalText,
    string? Error);
