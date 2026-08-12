namespace Workbench.Runtime.Agents;

public enum AgentSessionStatus
{
    Created,
    Ready,
    Running,
    WaitingApproval,
    Completed,
    Interrupted,
    Failed,
    Stopped,
    Archived
}
