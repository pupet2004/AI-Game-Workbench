using Workbench.Runtime.Providers;

namespace Workbench.Runtime.Agents;

public sealed record CreateAgentSessionRequest
{
    public CreateAgentSessionRequest(
        ProviderAccountId accountId,
        string modelId,
        string? workingDirectory = null,
        AgentAccessMode accessMode = AgentAccessMode.Restricted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        if (workingDirectory is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        }

        AccountId = accountId;
        ModelId = modelId;
        WorkingDirectory = workingDirectory;
        AccessMode = accessMode;
    }

    public ProviderAccountId AccountId { get; }

    public string ModelId { get; }

    public string? WorkingDirectory { get; }

    public AgentAccessMode AccessMode { get; }
}
