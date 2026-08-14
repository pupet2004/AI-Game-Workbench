using System.Text;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;
using Workbench.App.Memory;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Leader;

public enum LeaderHandoffSource
{
    None,
    Semantic,
    Fallback
}

public sealed record LeaderSessionRolloverResult(
    AgentSession NewSession,
    StoredLeaderSessionEpoch NewEpoch,
    string HandoffSummary,
    LeaderHandoffSource HandoffSource);

public sealed class LeaderSessionRolloverService(
    AgentRuntimeRegistry runtimeRegistry,
    ProjectLeaderRepository leaders,
    LeaderMessageRepository messages,
    TimeProvider timeProvider)
{
    private readonly AgentRuntimeRegistry _runtimeRegistry = runtimeRegistry ?? throw new ArgumentNullException(nameof(runtimeRegistry));
    private readonly ProjectLeaderRepository _leaders = leaders ?? throw new ArgumentNullException(nameof(leaders));
    private readonly LeaderMessageRepository _messages = messages ?? throw new ArgumentNullException(nameof(messages));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<LeaderSessionRolloverResult> RolloverAsync(
        CoreProject project,
        AgentSession oldSession,
        StoredLeaderSessionEpoch oldEpoch,
        bool resumeOldSession,
        string rolloverReason,
        LeaderMemoryPolicyDecision? policyDecision = null,
        bool policyManaged = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(oldSession);
        ArgumentNullException.ThrowIfNull(oldEpoch);
        ArgumentException.ThrowIfNullOrWhiteSpace(rolloverReason);
        if (oldEpoch.ProjectId != project.Id || oldEpoch.EndedAt is not null)
        {
            throw new InvalidOperationException("Rollover requires the active epoch owned by the Project Leader.");
        }

        var runtime = _runtimeRegistry.GetByAccount(oldSession.AccountId);
        string? handoff = policyDecision?.BrainHandoff;
        var source = handoff is null ? LeaderHandoffSource.None : LeaderHandoffSource.Semantic;
        if (!policyManaged)
        {
            handoff = await TryCreateSemanticHandoffAsync(runtime, oldSession, resumeOldSession, cancellationToken);
            source = LeaderHandoffSource.Semantic;
            if (handoff is null)
            {
                source = LeaderHandoffSource.Fallback;
                handoff = LeaderHandoffBuilder.BuildFallback(
                    project,
                    oldEpoch,
                    await _messages.GetAllAsync(oldEpoch.Id, cancellationToken));
            }
        }

        var newSession = await runtime.CreateSessionAsync(
            new CreateAgentSessionRequest(oldSession.AccountId, oldEpoch.ModelId, oldEpoch.WorkingDirectory),
            cancellationToken);
        if (newSession.Id == oldSession.Id ||
            string.IsNullOrWhiteSpace(newSession.ExternalSessionId) ||
            string.Equals(newSession.ExternalSessionId, oldSession.ExternalSessionId, StringComparison.Ordinal) ||
            newSession.AccountId != oldSession.AccountId ||
            newSession.ProviderId != oldSession.ProviderId ||
            !string.Equals(newSession.ModelId, oldEpoch.ModelId, StringComparison.Ordinal) ||
            !string.Equals(newSession.WorkingDirectory, oldEpoch.WorkingDirectory, StringComparison.Ordinal))
        {
            if (newSession.Id != oldSession.Id)
            {
                await StopIgnoringFailureAsync(runtime, newSession);
            }
            throw new InvalidOperationException(
                "The runtime did not create a distinct successor session with the inherited provider, account, model, and working directory.");
        }

        var now = _timeProvider.GetUtcNow();
        var newEpoch = new StoredLeaderSessionEpoch(
            Guid.NewGuid(),
            project.Id,
            oldEpoch.ProviderId,
            oldEpoch.ProviderAccountId,
            oldEpoch.ModelId,
            newSession.Id.Value,
            newSession.ExternalSessionId,
            oldEpoch.WorkingDirectory,
            now,
            now,
            null,
            null,
            null);

        try
        {
            var plan = policyDecision is { ContinuitySelection.Count: > 0, TotalContinuityBudgetUtf8Bytes: > 0 }
                ? new LeaderEpochContinuityPlan(newEpoch.Id, policyDecision.TotalContinuityBudgetUtf8Bytes.Value, policyDecision.ContinuitySelection, now)
                : null;
            await _leaders.RolloverAsync(
                project.Id,
                oldEpoch.Id,
                newEpoch,
                now,
                rolloverReason,
                handoff,
                plan,
                cancellationToken);
        }
        catch
        {
            try
            {
                await runtime.StopAsync(newSession, CancellationToken.None);
            }
            catch
            {
                // Best-effort cleanup must not replace the persistence failure.
            }

            throw;
        }

        return new LeaderSessionRolloverResult(newSession, newEpoch, handoff ?? string.Empty, source);
    }

    private static async Task<string?> TryCreateSemanticHandoffAsync(
        Workbench.Runtime.Runtime.IAgentRuntime runtime,
        AgentSession oldSession,
        bool resumeOldSession,
        CancellationToken cancellationToken)
    {
        AgentSession session;
        try
        {
            session = resumeOldSession
                ? await runtime.ResumeSessionAsync(oldSession, cancellationToken)
                : oldSession;
        }
        catch
        {
            return null;
        }

        try
        {
            await foreach (var agentEvent in runtime.SendAsync(
                               session,
                               new AgentRequest(LeaderHandoffBuilder.SemanticPrompt),
                               cancellationToken))
            {
                if (agentEvent is AgentApprovalRequested)
                {
                    await StopIgnoringFailureAsync(runtime, session);
                    return null;
                }

                if (agentEvent is AgentError or AgentToolEvent)
                {
                    await StopIgnoringFailureAsync(runtime, session);
                    return null;
                }

                if (agentEvent is AgentTurnCompleted completed)
                {
                    var text = completed.Result.FinalText;
                    if (completed.Result.FinalStatus != AgentSessionStatus.Completed ||
                        string.IsNullOrWhiteSpace(text) ||
                        Encoding.UTF8.GetByteCount(text) > LeaderHandoffBuilder.MaxHandoffUtf8Bytes)
                    {
                        return null;
                    }

                    return text;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static async Task StopIgnoringFailureAsync(
        Workbench.Runtime.Runtime.IAgentRuntime runtime,
        AgentSession session)
    {
        try
        {
            await runtime.StopAsync(session, CancellationToken.None);
        }
        catch
        {
        }
    }
}
