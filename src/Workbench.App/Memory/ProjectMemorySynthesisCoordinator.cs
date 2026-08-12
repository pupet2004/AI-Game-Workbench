using System.Collections.Concurrent;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Registry;
using Workbench.Runtime.Runtime;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;

namespace Workbench.App.Memory;

public enum ProjectMemorySynthesisRunResult
{
    NoPendingJob,
    AlreadyRunning,
    Completed,
    Deferred
}

public sealed class ProjectMemorySynthesisCoordinator(
    AgentRuntimeRegistry runtimeRegistry,
    ProjectMemorySynthesisRepository jobs,
    LeaderSessionEpochRepository epochs,
    LeaderMessageRepository messages,
    ProjectMemoryRepository memories,
    TimeProvider? timeProvider = null)
{
    private readonly AgentRuntimeRegistry _runtimeRegistry = runtimeRegistry ?? throw new ArgumentNullException(nameof(runtimeRegistry));
    private readonly ProjectMemorySynthesisRepository _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    private readonly LeaderSessionEpochRepository _epochs = epochs ?? throw new ArgumentNullException(nameof(epochs));
    private readonly LeaderMessageRepository _messages = messages ?? throw new ArgumentNullException(nameof(messages));
    private readonly ProjectMemoryRepository _memories = memories ?? throw new ArgumentNullException(nameof(memories));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _projectGates = [];

    public async Task<ProjectMemorySynthesisRunResult> TryProcessNextAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var gate = _projectGates.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken))
        {
            return ProjectMemorySynthesisRunResult.AlreadyRunning;
        }

        ProjectMemorySynthesisJob? job = null;
        IAgentRuntime? runtime = null;
        AgentSession? resumed = null;
        try
        {
            job = await _jobs.ClaimNextPendingAsync(projectId, cancellationToken);
            if (job is null)
            {
                return ProjectMemorySynthesisRunResult.NoPendingJob;
            }

            var epoch = await _epochs.GetAsync(job.EpochId, cancellationToken)
                ?? throw new InvalidOperationException("The archived Leader epoch no longer exists.");
            ValidateEpoch(job, epoch);
            runtime = _runtimeRegistry.GetByAccount(new ProviderAccountId(epoch.ProviderAccountId));
            if (!string.Equals(runtime.Provider.Id.Value, epoch.ProviderId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The persisted Provider does not match the registered account runtime.");
            }

            var archivedSession = Rehydrate(epoch);
            resumed = await runtime.ResumeSessionAsync(archivedSession, cancellationToken);
            ValidateResumedSession(archivedSession, resumed);

            var epochMessages = await _messages.GetAllAsync(epoch.Id, cancellationToken);
            var messagesBySequence = epochMessages.ToDictionary(message => message.Sequence);
            var formal = await _memories.GetAsync(projectId, "Formal", "Active", cancellationToken);
            var learned = await _memories.GetAsync(projectId, "Learned", "Active", cancellationToken);
            var prompt = ProjectMemorySynthesisPromptBuilder.Build(formal, learned, epochMessages);
            var finalText = await RunMaintenanceTurnAsync(runtime, resumed, prompt, cancellationToken);
            var payload = ProjectMemorySynthesisPayloadParser.Parse(finalText, messagesBySequence);
            await _memories.ApplySynthesisAsync(
                new ProjectMemorySynthesisApplication(
                    epoch.Id,
                    projectId,
                    payload.Learned,
                    payload.Candidates,
                    _timeProvider.GetUtcNow()),
                cancellationToken);
            return ProjectMemorySynthesisRunResult.Completed;
        }
        catch (Exception exception) when (job is not null)
        {
            if (runtime is not null && resumed is not null)
            {
                await StopIgnoringFailureAsync(runtime, resumed);
            }

            await ReturnToPendingIgnoringCancellationAsync(job.EpochId, Describe(exception));
            return ProjectMemorySynthesisRunResult.Deferred;
        }
        finally
        {
            gate.Release();
        }
    }

    private static AgentSession Rehydrate(StoredLeaderSessionEpoch epoch)
    {
        if (string.IsNullOrWhiteSpace(epoch.ExternalSessionId))
        {
            throw new InvalidOperationException("The archived Leader epoch has no external session identity.");
        }

        return new AgentSession(
            new AgentSessionId(epoch.AgentSessionId),
            new ProviderAccountId(epoch.ProviderAccountId),
            new ProviderId(epoch.ProviderId),
            epoch.ModelId,
            epoch.WorkingDirectory,
            epoch.ExternalSessionId,
            AgentSessionStatus.Archived,
            epoch.StartedAt,
            epoch.LastActiveAt);
    }

    private static void ValidateEpoch(ProjectMemorySynthesisJob job, StoredLeaderSessionEpoch epoch)
    {
        if (epoch.Id != job.EpochId || epoch.ProjectId != job.ProjectId || epoch.EndedAt is null)
        {
            throw new InvalidOperationException("Memory synthesis requires the queued archived epoch from the same project.");
        }
    }

    private static void ValidateResumedSession(AgentSession expected, AgentSession resumed)
    {
        if (resumed.Id != expected.Id ||
            resumed.AccountId != expected.AccountId ||
            resumed.ProviderId != expected.ProviderId ||
            !string.Equals(resumed.ModelId, expected.ModelId, StringComparison.Ordinal) ||
            !string.Equals(resumed.ExternalSessionId, expected.ExternalSessionId, StringComparison.Ordinal) ||
            !string.Equals(resumed.WorkingDirectory, expected.WorkingDirectory, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The runtime did not resume the exact archived AgentSession identity.");
        }
    }

    private static async Task<string> RunMaintenanceTurnAsync(
        IAgentRuntime runtime,
        AgentSession session,
        string prompt,
        CancellationToken cancellationToken)
    {
        string? finalText = null;
        await foreach (var agentEvent in runtime.SendAsync(session, new AgentRequest(prompt), cancellationToken))
        {
            switch (agentEvent)
            {
                case AgentApprovalRequested:
                    throw new InvalidOperationException("Memory synthesis requested approval.");
                case AgentToolEvent:
                    throw new InvalidOperationException("Memory synthesis attempted tool activity.");
                case AgentError error:
                    throw new InvalidOperationException($"Memory synthesis runtime error: {error.Message}");
                case AgentTurnCompleted completed:
                    if (completed.Result.FinalStatus != AgentSessionStatus.Completed ||
                        string.IsNullOrWhiteSpace(completed.Result.FinalText))
                    {
                        throw new InvalidOperationException("Memory synthesis did not complete with a payload.");
                    }
                    finalText = completed.Result.FinalText;
                    break;
            }
        }

        return finalText ?? throw new InvalidOperationException("Memory synthesis ended without completion.");
    }

    private async Task ReturnToPendingIgnoringCancellationAsync(Guid epochId, string error)
    {
        try
        {
            await _jobs.ReturnToPendingAsync(epochId, error, CancellationToken.None);
        }
        catch
        {
            // A startup recovery pass returns any surviving Running job to Pending.
        }
    }

    private static async Task StopIgnoringFailureAsync(IAgentRuntime runtime, AgentSession session)
    {
        try
        {
            await runtime.StopAsync(session, CancellationToken.None);
        }
        catch
        {
        }
    }

    private static string Describe(Exception exception) => exception switch
    {
        OperationCanceledException => "Memory synthesis was interrupted.",
        _ => string.IsNullOrWhiteSpace(exception.Message)
            ? "Memory synthesis failed."
            : exception.Message
    };
}
