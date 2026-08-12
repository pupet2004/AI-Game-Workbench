using Workbench.Runtime.Providers;

namespace Workbench.Runtime.Agents;

public sealed record CreateAgentSessionRequest
{
    public CreateAgentSessionRequest(ProviderAccountId accountId, string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        AccountId = accountId;
        ModelId = modelId;
    }

    public ProviderAccountId AccountId { get; }

    public string ModelId { get; }
}
