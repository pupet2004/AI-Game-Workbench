using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;

namespace Workbench.Runtime.Runtime;

public interface IAgentRuntime
{
    string RuntimeKind { get; }

    ProviderDescriptor Provider { get; }

    ProviderAccountSummary Account { get; }

    AgentCapability Capabilities { get; }

    bool HasActiveTurns => false;

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

    Task SteerAsync(
        AgentSession session,
        AgentRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException($"Runtime '{RuntimeKind}' does not support steering."));

    Task RespondToApprovalAsync(
        AgentSession session,
        AgentApprovalDecision decision,
        CancellationToken cancellationToken = default);

    Task RespondToQuestionAsync(
        AgentSession session,
        string requestId,
        IReadOnlyDictionary<string, string> answers,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException($"Runtime '{RuntimeKind}' does not support user questions."));

    Task StopAsync(AgentSession session, CancellationToken cancellationToken = default);

    Task<AgentSessionStatus> GetStatusAsync(
        AgentSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentEvent>> GetTranscriptAsync(
        AgentSession session,
        CancellationToken cancellationToken = default);
}
