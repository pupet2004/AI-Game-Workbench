namespace Workbench.Runtime.Agents;

public sealed record AgentApprovalDecision
{
    public AgentApprovalDecision(AgentApprovalRequestId requestId, string optionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(optionId);

        RequestId = requestId;
        OptionId = optionId;
    }

    public AgentApprovalRequestId RequestId { get; }

    public string OptionId { get; }
}
