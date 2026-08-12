using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;

namespace Workbench.Runtime.Runtime;

public interface IAgentRuntime
{
    ProviderDescriptor Provider { get; }

    ProviderAccountSummary Account { get; }

    AgentCapability Capabilities { get; }

    Task<IReadOnlyList<ModelProfile>> GetModelsAsync(CancellationToken cancellationToken = default);

    Task<AgentSession> CreateSessionAsync(
        CreateAgentSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentSession> ResumeSessionAsync(
        AgentSession session,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgentEvent> SendAsync(
        AgentSession session,
        AgentRequest request,
        CancellationToken cancellationToken = default);

    Task StopAsync(AgentSession session, CancellationToken cancellationToken = default);

    Task<AgentSessionStatus> GetStatusAsync(
        AgentSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentEvent>> GetTranscriptAsync(
        AgentSession session,
        CancellationToken cancellationToken = default);
}
