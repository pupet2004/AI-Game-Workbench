using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.Runtime.Runtime;

namespace Workbench.App.AgentHost;

/// <summary>
/// The origin is kept separate from the provider message role. A hosted
/// session can therefore distinguish user direction from Leader direction
/// without giving a Surface ownership of the runtime writer.
/// </summary>
public enum AgentIntentSource
{
    User,
    Leader,
    Workbench
}

public sealed record HostedAgentIntent(
    AgentIntentSource Source,
    string Text,
    IReadOnlyList<AgentInputPart>? Inputs = null,
    string? OutputSchema = null,
    AgentAccessMode AccessMode = AgentAccessMode.Restricted,
    DateTimeOffset? SubmittedAt = null,
    string? DisplayText = null)
{
    public DateTimeOffset EffectiveSubmittedAt => SubmittedAt ?? DateTimeOffset.UtcNow;
}

public sealed record HostedAgentEvent(AgentSessionId SessionId, AgentEvent Event);

public sealed record HostedAgentIntentReceived(AgentSessionId SessionId, HostedAgentIntent Intent);

public sealed record HostedAgentSessionSnapshot(
    AgentSession Session,
    bool HasActiveTurn,
    IReadOnlyList<HostedAgentIntent> Intents,
    IReadOnlyList<AgentEvent> Events);

/// <summary>
/// Logical owner of Agent sessions in the current process. Surfaces attach to
/// this service; they never talk to a provider runtime directly.
/// </summary>
public interface IAgentHost
{
    event EventHandler<HostedAgentEvent>? EventReceived;
    event EventHandler<HostedAgentIntentReceived>? IntentReceived;

    void RegisterRuntime(IAgentRuntime runtime);

    void RegisterSession(AgentSession session);

    Task<AgentSession> CreateSessionAsync(
        CreateAgentSessionRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentSession> ResumeSessionAsync(
        AgentSession session,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgentEvent> RunTurnAsync(
        AgentSession session,
        HostedAgentIntent intent,
        CancellationToken cancellationToken = default);

    Task SteerAsync(
        AgentSession session,
        HostedAgentIntent intent,
        CancellationToken cancellationToken = default);

    Task StopAsync(AgentSession session, CancellationToken cancellationToken = default);

    Task RespondToApprovalAsync(
        AgentSession session,
        AgentApprovalDecision decision,
        CancellationToken cancellationToken = default);

    Task RespondToApprovalAsync(
        AgentSessionId sessionId,
        AgentApprovalDecision decision,
        CancellationToken cancellationToken = default);

    Task RespondToQuestionAsync(
        AgentSession session,
        string requestId,
        IReadOnlyDictionary<string, string> answers,
        CancellationToken cancellationToken = default);

    Task<AgentSessionStatus> GetStatusAsync(
        AgentSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentEvent>> GetTranscriptAsync(
        AgentSession session,
        CancellationToken cancellationToken = default);

    HostedAgentSessionSnapshot Attach(AgentSession session);
}

public sealed class InProcessAgentHost(AgentRuntimeRegistry runtimes) : IAgentHost
{
    private const int HistoryLimit = 2_000;
    private readonly AgentRuntimeRegistry _runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
    private readonly ConcurrentDictionary<AgentSessionId, HostedSession> _sessions = [];

    public event EventHandler<HostedAgentEvent>? EventReceived;
    public event EventHandler<HostedAgentIntentReceived>? IntentReceived;

    public void RegisterRuntime(IAgentRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!_runtimes.TryGetByAccount(runtime.Account.Id, out _))
        {
            _runtimes.Register(runtime);
        }
    }

    public void RegisterSession(AgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var hosted = _sessions.GetOrAdd(session.Id, static (_, value) => new HostedSession(value), session);
        hosted.UpdateSession(session);
    }

    public async Task<AgentSession> CreateSessionAsync(
        CreateAgentSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var runtime = _runtimes.GetByAccount(request.AccountId);
        var session = await runtime.CreateSessionAsync(request, cancellationToken).ConfigureAwait(false);
        RegisterSession(session);
        return session;
    }

    public async Task<AgentSession> ResumeSessionAsync(
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        var runtime = _runtimes.GetByAccount(session.AccountId);
        var resumed = await runtime.ResumeSessionAsync(session, cancellationToken).ConfigureAwait(false);
        RegisterSession(resumed);
        return resumed;
    }

    public async IAsyncEnumerable<AgentEvent> RunTurnAsync(
        AgentSession session,
        HostedAgentIntent intent,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent.Text);

        var hosted = GetOrRegister(session);
        await hosted.Writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        hosted.IsTurnActive = true;
        var recordedIntent = hosted.RecordIntent(intent);
        IntentReceived?.Invoke(this, new HostedAgentIntentReceived(session.Id, recordedIntent));
        var terminalEventPublished = false;
        try
        {
            var runtime = _runtimes.GetByAccount(session.AccountId);
            await using var runtimeEvents = runtime.SendAsync(
                session,
                CreateRequest(intent),
                cancellationToken).GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await runtimeEvents.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (!terminalEventPublished)
                {
                    // Provider transports can fail before sending
                    // turn/completed. A hosted surface must still leave
                    // Working immediately instead of waiting forever for an
                    // event that will never arrive.
                    var failed = new AgentTurnCompleted(
                        new AgentResult(session.Id, AgentSessionStatus.Failed, null, exception.Message),
                        DateTimeOffset.UtcNow);
                    hosted.RecordEvent(failed);
                    EventReceived?.Invoke(this, new HostedAgentEvent(session.Id, failed));
                    throw;
                }

                if (!hasNext)
                    break;

                var agentEvent = runtimeEvents.Current;
                terminalEventPublished |= agentEvent is AgentTurnCompleted;
                hosted.RecordEvent(agentEvent);
                EventReceived?.Invoke(this, new HostedAgentEvent(session.Id, agentEvent));
                foreach (var progress in hosted.ExtractProgress(agentEvent))
                {
                    hosted.RecordEvent(progress);
                    EventReceived?.Invoke(this, new HostedAgentEvent(session.Id, progress));
                    yield return progress;
                }
                yield return agentEvent;
            }
        }
        finally
        {
            hosted.IsTurnActive = false;
            hosted.Writer.Release();
        }
    }

    public async Task SteerAsync(
        AgentSession session,
        HostedAgentIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent.Text);
        var hosted = GetOrRegister(session);
        if (!hosted.IsTurnActive)
        {
            throw new InvalidOperationException("The hosted session has no active turn to steer.");
        }

        await hosted.ControlWriter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var runtime = _runtimes.GetByAccount(session.AccountId);
            await runtime.SteerAsync(session, CreateRequest(intent), cancellationToken).ConfigureAwait(false);
            var recordedIntent = hosted.RecordIntent(intent);
            IntentReceived?.Invoke(this, new HostedAgentIntentReceived(session.Id, recordedIntent));
        }
        finally
        {
            hosted.ControlWriter.Release();
        }
    }

    public Task StopAsync(AgentSession session, CancellationToken cancellationToken = default) =>
        _runtimes.GetByAccount(session.AccountId).StopAsync(session, cancellationToken);

    public Task RespondToApprovalAsync(
        AgentSession session,
        AgentApprovalDecision decision,
        CancellationToken cancellationToken = default) =>
        _runtimes.GetByAccount(session.AccountId).RespondToApprovalAsync(session, decision, cancellationToken);

    public Task RespondToApprovalAsync(
        AgentSessionId sessionId,
        AgentApprovalDecision decision,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var hosted))
            hosted = _sessions.Values.FirstOrDefault(item => item.ContainsApproval(decision.RequestId));
        if (hosted is null)
            throw new InvalidOperationException($"Hosted session '{sessionId}' is not registered.");
        return RespondToApprovalAsync(hosted.Session, decision, cancellationToken);
    }

    public Task RespondToQuestionAsync(
        AgentSession session,
        string requestId,
        IReadOnlyDictionary<string, string> answers,
        CancellationToken cancellationToken = default) =>
        _runtimes.GetByAccount(session.AccountId).RespondToQuestionAsync(session, requestId, answers, cancellationToken);

    public async Task<AgentSessionStatus> GetStatusAsync(
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        var hosted = GetOrRegister(session);
        var hostedStatus = hosted.Session.Status;
        if (hosted.IsTurnActive || hostedStatus is
            AgentSessionStatus.Completed or
            AgentSessionStatus.Failed or
            AgentSessionStatus.Interrupted or
            AgentSessionStatus.Stopped or
            AgentSessionStatus.Archived)
            return hostedStatus;

        return await _runtimes.GetByAccount(session.AccountId)
            .GetStatusAsync(hosted.Session, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentEvent>> GetTranscriptAsync(
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        var hosted = GetOrRegister(session);
        var runtime = _runtimes.GetByAccount(session.AccountId);
        var providerEvents = await runtime.GetTranscriptAsync(session, cancellationToken).ConfigureAwait(false);
        if (providerEvents.Count > 0)
        {
            return providerEvents;
        }

        return hosted.GetEvents();
    }

    public HostedAgentSessionSnapshot Attach(AgentSession session)
    {
        var hosted = GetOrRegister(session);
        return hosted.Snapshot();
    }

    private HostedSession GetOrRegister(AgentSession session)
    {
        RegisterSession(session);
        return _sessions[session.Id];
    }

    private static AgentRequest CreateRequest(HostedAgentIntent intent) =>
        new(intent.Text, intent.OutputSchema, intent.Inputs, intent.AccessMode, intent.Source switch
        {
            AgentIntentSource.User => AgentRequestSource.User,
            AgentIntentSource.Leader => AgentRequestSource.Leader,
            _ => AgentRequestSource.Workbench
        });

    private sealed class HostedSession(AgentSession session)
    {
        private readonly object _sync = new();
        private readonly List<HostedAgentIntent> _intents = [];
        private readonly List<AgentEvent> _events = [];
        private readonly StringBuilder _progressBuffer = new();

        public SemaphoreSlim Writer { get; } = new(1, 1);
        public SemaphoreSlim ControlWriter { get; } = new(1, 1);
        public AgentSession Session { get; private set; } = session;
        public bool IsTurnActive { get; set; }

        public void UpdateSession(AgentSession session)
        {
            lock (_sync)
            {
                // Attach/Register may receive a persisted snapshot that is
                // older than a completion event already observed in-process.
                // Never regress the hosted status with that stale snapshot.
                if (session.UpdatedAt >= Session.UpdatedAt)
                    Session = session;
            }
        }

        public HostedAgentIntent RecordIntent(HostedAgentIntent intent)
        {
            var recorded = intent with { SubmittedAt = intent.EffectiveSubmittedAt };
            lock (_sync)
            {
                _intents.Add(recorded);
                Trim(_intents);
            }
            return recorded;
        }

        public void RecordEvent(AgentEvent agentEvent)
        {
            lock (_sync)
            {
                _events.Add(agentEvent);
                Session = agentEvent switch
                {
                    AgentStatusChanged status => Session with { Status = status.Status, UpdatedAt = status.OccurredAt },
                    AgentApprovalRequested approval => Session with { Status = AgentSessionStatus.WaitingApproval, UpdatedAt = approval.OccurredAt },
                    AgentQuestionRequested question => Session with { Status = AgentSessionStatus.WaitingApproval, UpdatedAt = question.OccurredAt },
                    AgentTurnCompleted completed => Session with { Status = completed.Result.FinalStatus, UpdatedAt = completed.OccurredAt },
                    _ => Session
                };
                if (agentEvent is AgentTurnCompleted)
                {
                    // The provider turn has ended even if the Worker Router
                    // still has Handoff/evidence work to persist afterward.
                    IsTurnActive = false;
                }
                Trim(_events);
            }
        }

        public IReadOnlyList<AgentProgressChanged> ExtractProgress(AgentEvent agentEvent)
        {
            if (agentEvent is not AgentTextDelta delta || string.IsNullOrEmpty(delta.Text)) return [];
            lock (_sync)
            {
                _progressBuffer.Append(delta.Text);
                var results = new List<AgentProgressChanged>();
                while (AgentProgressProtocol.TryConsumeNextMarker(_progressBuffer, out var completed))
                    results.Add(new AgentProgressChanged(completed, delta.OccurredAt));

                if (_progressBuffer.Length > 1024)
                    _progressBuffer.Clear();
                return results;
            }
        }

        public IReadOnlyList<AgentEvent> GetEvents()
        {
            lock (_sync)
            {
                return _events.ToArray();
            }
        }

        public bool ContainsApproval(AgentApprovalRequestId requestId)
        {
            lock (_sync)
            {
                return _events.OfType<AgentApprovalRequested>().Any(item => item.RequestId == requestId);
            }
        }

        public HostedAgentSessionSnapshot Snapshot()
        {
            lock (_sync)
            {
                return new HostedAgentSessionSnapshot(Session, IsTurnActive, _intents.ToArray(), _events.ToArray());
            }
        }

        private static void Trim<T>(List<T> values)
        {
            var overflow = values.Count - HistoryLimit;
            if (overflow > 0)
            {
                values.RemoveRange(0, overflow);
            }
        }
    }
}
