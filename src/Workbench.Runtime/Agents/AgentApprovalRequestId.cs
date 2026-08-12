namespace Workbench.Runtime.Agents;

public readonly record struct AgentApprovalRequestId(Guid Value)
{
    public static AgentApprovalRequestId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
