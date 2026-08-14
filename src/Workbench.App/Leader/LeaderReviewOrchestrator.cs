using Workbench.Core.Tasks;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Registry;
using Workbench.Runtime.Runtime;
using Workbench.Storage.Leaders;
using Workbench.Storage.Tasks;

namespace Workbench.App.Leader;

public enum LeaderReviewOrchestrationResultKind
{
    NoWork,
    ExistingDecision,
    ValidationFailure,
    RuntimeFailure,
    StructuredOutputFailure,
    PersistenceFailure,
    Recorded
}

public sealed record LeaderReviewOrchestrationResult(LeaderReviewOrchestrationResultKind Kind, StoredLeaderReviewDecision? Decision = null, string? Error = null);

public interface ILeaderReviewOrchestrator
{
    Task<LeaderReviewOrchestrationResult> TryReviewAsync(Guid projectId, Guid taskId, Guid finalReportEventId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LeaderReviewOrchestrationResult>> RecoverAsync(Guid projectId, CancellationToken cancellationToken = default);
}

public sealed class LeaderReviewOrchestrator(
    LeaderReviewInputBuilder inputBuilder,
    LeaderReviewRuntimeAdapter runtimeAdapter,
    AssignmentReviewStateRepository reviewState,
    TaskRepository tasks,
    ProjectLeaderRepository leaders,
    LeaderSessionEpochRepository epochs,
    AgentRuntimeRegistry runtimes,
    TimeProvider timeProvider,
    ILeaderReviewAutoProceedExecutor? autoProceed = null) : ILeaderReviewOrchestrator
{
    public async Task<LeaderReviewOrchestrationResult> TryReviewAsync(Guid projectId, Guid taskId, Guid finalReportEventId, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || taskId == Guid.Empty || finalReportEventId == Guid.Empty) return new(LeaderReviewOrchestrationResultKind.NoWork);

        var task = await tasks.GetAsync(projectId, taskId, cancellationToken);
        if (task is null || task.Status != TaskLifecycleStatus.Reviewing) return new(LeaderReviewOrchestrationResultKind.NoWork);
        var existing = await reviewState.GetLeaderReviewDecisionAsync(projectId, taskId, task.CurrentRevisionId, finalReportEventId, cancellationToken);
        if (existing is not null)
        {
            if (autoProceed is not null) await autoProceed.TryExecuteAsync(projectId, taskId, existing.TaskRevisionId, existing.FinalReportEventId, cancellationToken);
            return new(LeaderReviewOrchestrationResultKind.ExistingDecision, existing);
        }

        var input = await inputBuilder.BuildAsync(projectId, taskId, finalReportEventId, cancellationToken);
        if (input is null) return new(LeaderReviewOrchestrationResultKind.ValidationFailure);

        var leader = await leaders.GetAsync(projectId, cancellationToken);
        var epoch = leader?.CurrentEpochId is Guid epochId ? await epochs.GetAsync(epochId, cancellationToken) : null;
        if (epoch is null || epoch.ProjectId != projectId || epoch.EndedAt is not null || epoch.AgentSessionId == Guid.Empty)
            return new(LeaderReviewOrchestrationResultKind.RuntimeFailure, Error: "The existing Leader session is unavailable.");

        IAgentRuntime runtime;
        try { runtime = runtimes.GetByAccount(new ProviderAccountId(epoch.ProviderAccountId)); }
        catch (Exception exception) when (exception is KeyNotFoundException or FormatException)
        { return new(LeaderReviewOrchestrationResultKind.RuntimeFailure, Error: "The existing Leader runtime is unavailable."); }
        if (runtime.Provider.Id.Value != epoch.ProviderId)
            return new(LeaderReviewOrchestrationResultKind.RuntimeFailure, Error: "The existing Leader runtime is unavailable.");

        var session = new AgentSession(new AgentSessionId(epoch.AgentSessionId), new ProviderAccountId(epoch.ProviderAccountId), new ProviderId(epoch.ProviderId), epoch.ModelId, epoch.WorkingDirectory, epoch.ExternalSessionId, AgentSessionStatus.Ready, epoch.StartedAt, epoch.LastActiveAt);
        var runtimeResult = await runtimeAdapter.ReviewAsync(input, runtime, session, cancellationToken);
        if (!runtimeResult.Succeeded) return new(runtimeResult.FailureKind switch { LeaderReviewFailureKind.StructuredOutput => LeaderReviewOrchestrationResultKind.StructuredOutputFailure, LeaderReviewFailureKind.Validation => LeaderReviewOrchestrationResultKind.ValidationFailure, _ => LeaderReviewOrchestrationResultKind.RuntimeFailure }, Error: runtimeResult.Error);

        var decision = runtimeResult.Decision!;
        try
        {
            var persisted = await reviewState.TryRecordLeaderReviewDecisionAsync(new LeaderReviewDecisionPersistenceRequest(Guid.NewGuid(), projectId, taskId, decision.TaskRevisionId, decision.FinalReportEventId, decision.Outcome.ToString(), decision.ActionLevel.ToString(), decision.ReviewDepth.ToString(), decision.Summary, decision.Issue, decision.NextAction, decision.ImportantNote, timeProvider.GetUtcNow()), cancellationToken);
            if (persisted == AssignmentStateTransitionResult.Applied)
            {
                var stored = await reviewState.GetLeaderReviewDecisionAsync(projectId, taskId, decision.TaskRevisionId, decision.FinalReportEventId, cancellationToken);
                if (stored is not null && autoProceed is not null) await autoProceed.TryExecuteAsync(projectId, taskId, stored.TaskRevisionId, stored.FinalReportEventId, cancellationToken);
                return new(LeaderReviewOrchestrationResultKind.Recorded, stored);
            }
            if (persisted == AssignmentStateTransitionResult.Idempotent)
            {
                var stored = await reviewState.GetLeaderReviewDecisionAsync(projectId, taskId, decision.TaskRevisionId, decision.FinalReportEventId, cancellationToken);
                if (stored is not null && autoProceed is not null) await autoProceed.TryExecuteAsync(projectId, taskId, stored.TaskRevisionId, stored.FinalReportEventId, cancellationToken);
                return new(LeaderReviewOrchestrationResultKind.ExistingDecision, stored);
            }
            return new(LeaderReviewOrchestrationResultKind.PersistenceFailure, Error: persisted.ToString());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return new(LeaderReviewOrchestrationResultKind.PersistenceFailure, Error: "Review decision persistence failed."); }
    }

    public async Task<IReadOnlyList<LeaderReviewOrchestrationResult>> RecoverAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var pending = await reviewState.ListPendingReviewReportsAsync(projectId, cancellationToken);
        var results = new List<LeaderReviewOrchestrationResult>(pending.Count);
        foreach (var item in pending)
        {
            results.Add(await TryReviewAsync(projectId, item.TaskId, item.FinalReportEventId, cancellationToken));
        }
        return results;
    }
}
