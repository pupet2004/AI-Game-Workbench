using Workbench.Runtime.Agents;

namespace Workbench.Runtime.Providers;

public sealed record ModelProfile
{
    public ModelProfile(
        ProviderId providerId,
        string modelId,
        string displayName,
        AgentCapability capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        ProviderId = providerId;
        ModelId = modelId;
        DisplayName = displayName;
        Capabilities = capabilities;
    }

    public ProviderId ProviderId { get; }

    public string ModelId { get; }

    public string DisplayName { get; }

    public AgentCapability Capabilities { get; }
}
