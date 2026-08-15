using Workbench.Core.Tasks;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Registry;
using Workbench.Runtime.Runtime;
using Workbench.Storage.Leaders;
using Workbench.Storage.Tasks;
using Workbench.Storage.Reviews;
using Workbench.Storage.Settings;
using Workbench.Core.Leaders;

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
    LeaderReviewStateRepository typedReviewState,
    LeaderAuthoritySettingsService authoritySettings,
    TaskRepository tasks,
    ProjectLeaderRepository leaders,
    LeaderSessionEpochRepository epochs,
    AgentRuntimeRegistry runtimes,
    TimeProvider timeProvider,
    ILeaderReviewAutoProceedExecutor? autoProceed = null,
    ILeaderReviewAskUserGate? askUserGate = null) : ILeaderReviewOrchestrator
{
    public async Task<LeaderReviewOrchestrationResult> TryReviewAsync(Guid projectId, Guid taskId, Guid finalReportEventId, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || taskId == Guid.Empty || finalReportEventId == Guid.Empty) return new(LeaderReviewOrchestrationResultKind.NoWork);

        var task = await tasks.GetAsync(projectId, taskId, cancellationToken);
        if (task is null || task.Status != TaskLifecycleStatus.Reviewing) return new(LeaderReviewOrchestrationResultKind.NoWork);
        var existingTyped = await typedReviewState.GetDecisionByFinalReportAsync(projectId, taskId, finalReportEventId, cancellationToken);
        if (existingTyped is not null)
        {
            if (autoProceed is not null) await autoProceed.TryExecuteAsync(projectId, taskId, existingTyped.RevisionId, existingTyped.FinalReportEventId, cancellationToken);
            if (askUserGate is not null) await askUserGate.TryOpenAsync(projectId, taskId, existingTyped.RevisionId, existingTyped.FinalReportEventId, cancellationToken);
            return new(LeaderReviewOrchestrationResultKind.ExistingDecision);
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
            var authority = await authoritySettings.GetEffectiveLeaderAuthorityModeAsync(projectId, cancellationToken);
            var resolution = LeaderAuthorityResolver.Resolve(authority, decision.ActionLevel, decision.Outcome);
            var decisionId = Guid.NewGuid();
            var typed = await typedReviewState.InsertDecisionIfAbsentAsync(new LeaderReviewDecisionWriteRequest(decisionId, projectId, taskId, decision.TaskRevisionId, decision.FinalReportEventId, decision.Outcome.ToString(), decision.ActionLevel.ToString(), resolution.ToString(), authority, timeProvider.GetUtcNow()), cancellationToken);
            if (typed is LeaderReviewWriteResult.Conflict) return new(LeaderReviewOrchestrationResultKind.PersistenceFailure, Error: typed.ToString());
            var persisted = await reviewState.TryRecordLeaderReviewDecisionAsync(new LeaderReviewDecisionPersistenceRequest(decisionId, projectId, taskId, decision.TaskRevisionId, decision.FinalReportEventId, decision.Outcome.ToString(), decision.ActionLevel.ToString(), decision.ReviewDepth.ToString(), decision.Summary, decision.Issue, decision.NextAction, decision.ImportantNote, timeProvider.GetUtcNow()), cancellationToken);
            if (persisted == AssignmentStateTransitionResult.Applied)
            {
                if (autoProceed is not null) await autoProceed.TryExecuteAsync(projectId, taskId, decision.TaskRevisionId, decision.FinalReportEventId, cancellationToken);
                if (askUserGate is not null) await askUserGate.TryOpenAsync(projectId, taskId, decision.TaskRevisionId, decision.FinalReportEventId, cancellationToken);
                return new(LeaderReviewOrchestrationResultKind.Recorded);
            }
            if (persisted == AssignmentStateTransitionResult.Idempotent)
            {
                if (autoProceed is not null) await autoProceed.TryExecuteAsync(projectId, taskId, decision.TaskRevisionId, decision.FinalReportEventId, cancellationToken);
                if (askUserGate is not null) await askUserGate.TryOpenAsync(projectId, taskId, decision.TaskRevisionId, decision.FinalReportEventId, cancellationToken);
                return new(LeaderReviewOrchestrationResultKind.ExistingDecision);
            }
            return new(LeaderReviewOrchestrationResultKind.PersistenceFailure, Error: persisted.ToString());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return new(LeaderReviewOrchestrationResultKind.PersistenceFailure, Error: "Review decision persistence failed."); }
    }

    public async Task<IReadOnlyList<LeaderReviewOrchestrationResult>> RecoverAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var pending = await typedReviewState.ListPendingReviewSubjectsAsync(projectId, cancellationToken);
        var results = new List<LeaderReviewOrchestrationResult>(pending.Count);
        foreach (var item in pending)
        {
            results.Add(await TryReviewAsync(projectId, item.TaskId, item.FinalReportEventId, cancellationToken));
        }
        return results;
    }
}
