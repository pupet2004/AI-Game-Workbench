namespace Workbench.Runtime.Agents;

public sealed record AgentApprovalOption
{
    public AgentApprovalOption(string id, string label, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        Id = id;
        Label = label;
        Description = description;
    }

    public string Id { get; }

    public string Label { get; }

    public string? Description { get; }
}
