using System.Runtime.CompilerServices;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Runtime;

namespace Workbench.Runtime.Tests;

internal sealed class FakeAgentRuntime(
    ProviderDescriptor provider,
    ProviderAccountSummary account,
    IReadOnlyList<ModelProfile> models) : IAgentRuntime
{
    public ProviderDescriptor Provider { get; } = provider;

    public ProviderAccountSummary Account { get; } = account;

    public AgentCapability Capabilities { get; } =
        AgentCapability.PersistentSession |
        AgentCapability.Resume |
        AgentCapability.StructuredEvents |
        AgentCapability.Stop |
        AgentCapability.Transcript;

    public Task<IReadOnlyList<ModelProfile>> GetModelsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(models);

    public Task<AgentSession> CreateSessionAsync(
        CreateAgentSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(new AgentSession(
            AgentSessionId.New(),
            request.AccountId,
            Provider.Id,
            request.ModelId,
            null,
            AgentSessionStatus.Ready,
            now,
            now));
    }

    public Task<AgentSession> ResumeSessionAsync(
        AgentSession session,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(session);

    public async IAsyncEnumerable<AgentEvent> SendAsync(
        AgentSession session,
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task StopAsync(AgentSession session, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<AgentSessionStatus> GetStatusAsync(
        AgentSession session,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(session.Status);

    public Task<IReadOnlyList<AgentEvent>> GetTranscriptAsync(
        AgentSession session,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AgentEvent>>([]);
}
