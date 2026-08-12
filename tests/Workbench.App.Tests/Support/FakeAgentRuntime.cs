using System.Runtime.CompilerServices;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Runtime;

namespace Workbench.App.Tests.Support;

internal sealed class FakeAgentRuntime : IAgentRuntime, IAsyncDisposable
{
    private readonly Queue<IReadOnlyList<AgentEvent>> _turns = [];
    private TaskCompletionSource _sendRelease = NewSignal();
    private TaskCompletionSource _approvalRelease = NewSignal();
    private TaskCompletionSource _sendStarted = NewSignal();
    private TaskCompletionSource _approvalObserved = NewSignal();

    public FakeAgentRuntime(
        string providerName = "Fake Provider",
        string accountName = "Fake Account",
        ProviderAccountId? accountId = null,
        params ModelProfile[] models)
    {
        var providerId = new ProviderId(providerName.ToLowerInvariant().Replace(' ', '-'));
        Provider = new ProviderDescriptor(providerId, providerName);
        Account = new ProviderAccountSummary(accountId ?? ProviderAccountId.New(), providerId, accountName, true);
        Models = models.Length > 0
            ? models
            : [new ModelProfile(providerId, "model-a", "Model A", AgentCapability.StructuredEvents)];
    }

    public ProviderDescriptor Provider { get; }

    public ProviderAccountSummary Account { get; }

    public AgentCapability Capabilities { get; } =
        AgentCapability.PersistentSession |
        AgentCapability.StructuredEvents |
        AgentCapability.Stop |
        AgentCapability.Approval;

    public IReadOnlyList<ModelProfile> Models { get; }

    public List<CreateAgentSessionRequest> CreateRequests { get; } = [];

    public List<AgentSession> CreatedSessions { get; } = [];

    public List<AgentSession> SentSessions { get; } = [];

    public List<AgentSession> ResumedSessions { get; } = [];

    public List<AgentRequest> SentRequests { get; } = [];

    public List<AgentApprovalDecision> ApprovalDecisions { get; } = [];

    public List<AgentSession> StoppedSessions { get; } = [];

    public Exception? ModelException { get; set; }

    public Exception? SendException { get; set; }

    public Exception? CreateException { get; set; }

    public Exception? ResumeException { get; set; }

    public Exception? StopException { get; set; }

    public Func<CreateAgentSessionRequest, AgentSession>? CreateSessionOverride { get; set; }

    public int GetModelsCallCount { get; private set; }

    public Exception? ApprovalException { get; set; }

    public bool PauseBeforeEvents { get; set; }

    public bool PauseAfterApproval { get; set; }

    public bool IsDisposed { get; private set; }

    public void QueueTurn(params AgentEvent[] events) => _turns.Enqueue(events);

    public Task WaitForSendAsync() => _sendStarted.Task;

    public Task WaitForApprovalAsync() => _approvalObserved.Task;

    public void ReleaseSend() => _sendRelease.TrySetResult();

    public void ReleaseApproval() => _approvalRelease.TrySetResult();

    public void ResetTurnSignals()
    {
        _sendRelease = NewSignal();
        _approvalRelease = NewSignal();
        _sendStarted = NewSignal();
        _approvalObserved = NewSignal();
    }

    public Task<IReadOnlyList<ModelProfile>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        GetModelsCallCount++;
        if (ModelException is not null)
        {
            return Task.FromException<IReadOnlyList<ModelProfile>>(ModelException);
        }

        return Task.FromResult(Models);
    }

    public Task<AgentSession> CreateSessionAsync(
        CreateAgentSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (CreateException is not null)
        {
            return Task.FromException<AgentSession>(CreateException);
        }

        CreateRequests.Add(request);
        if (CreateSessionOverride is not null)
        {
            var overridden = CreateSessionOverride(request);
            CreatedSessions.Add(overridden);
            return Task.FromResult(overridden);
        }

        var now = DateTimeOffset.UtcNow;
        var session = new AgentSession(
            AgentSessionId.New(),
            request.AccountId,
            Provider.Id,
            request.ModelId,
            request.WorkingDirectory,
            $"thread-{CreatedSessions.Count + 1}",
            AgentSessionStatus.Ready,
            now,
            now);
        CreatedSessions.Add(session);
        return Task.FromResult(session);
    }

    public Task<AgentSession> ResumeSessionAsync(
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        if (ResumeException is not null)
        {
            return Task.FromException<AgentSession>(ResumeException);
        }

        ResumedSessions.Add(session);
        return Task.FromResult(session);
    }

    public async IAsyncEnumerable<AgentEvent> SendAsync(
        AgentSession session,
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        SentSessions.Add(session);
        SentRequests.Add(request);
        _sendStarted.TrySetResult();

        if (PauseBeforeEvents)
        {
            await _sendRelease.Task.WaitAsync(cancellationToken);
        }

        if (SendException is not null)
        {
            throw SendException;
        }

        var events = _turns.Count > 0 ? _turns.Dequeue() : [];
        foreach (var agentEvent in events)
        {
            yield return agentEvent;
            if (agentEvent is AgentApprovalRequested)
            {
                _approvalObserved.TrySetResult();
                if (PauseAfterApproval)
                {
                    await _approvalRelease.Task.WaitAsync(cancellationToken);
                }
            }
        }
    }

    public Task RespondToApprovalAsync(
        AgentSession session,
        AgentApprovalDecision decision,
        CancellationToken cancellationToken = default)
    {
        if (ApprovalException is not null)
        {
            return Task.FromException(ApprovalException);
        }

        ApprovalDecisions.Add(decision);
        _approvalRelease.TrySetResult();
        return Task.CompletedTask;
    }

    public Task StopAsync(AgentSession session, CancellationToken cancellationToken = default)
    {
        StoppedSessions.Add(session);
        if (StopException is not null)
        {
            return Task.FromException(StopException);
        }

        _sendRelease.TrySetResult();
        _approvalRelease.TrySetResult();
        return Task.CompletedTask;
    }

    public Task<AgentSessionStatus> GetStatusAsync(
        AgentSession session,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(session.Status);

    public Task<IReadOnlyList<AgentEvent>> GetTranscriptAsync(
        AgentSession session,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AgentEvent>>([]);

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
