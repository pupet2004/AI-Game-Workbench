namespace Workbench.Runtime.Agents;

[Flags]
public enum AgentCapability
{
    None = 0,
    PersistentSession = 1 << 0,
    Resume = 1 << 1,
    StructuredEvents = 1 << 2,
    Approval = 1 << 3,
    Stop = 1 << 4,
    Transcript = 1 << 5,
    ParallelSessions = 1 << 6,
    ToolEvents = 1 << 7,
    StructuredOutput = 1 << 8,
    CostMetrics = 1 << 9
}
