namespace Workbench.Runtime.Agents;

public readonly record struct AgentSessionId(Guid Value)
{
    public static AgentSessionId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
