using Workbench.Runtime.Runtime;
using Workbench.Storage.Settings;

namespace Workbench.App.Services;

public sealed record ConfiguredAgentRuntimeFactory
{
    public ConfiguredAgentRuntimeFactory(
        string agentId,
        Func<AgentRuntimeSettings, CancellationToken, Task<IAgentRuntime>> connectAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(connectAsync);
        AgentId = agentId;
        ConnectAsync = connectAsync;
    }

    public string AgentId { get; }

    public Func<AgentRuntimeSettings, CancellationToken, Task<IAgentRuntime>> ConnectAsync { get; }
}
