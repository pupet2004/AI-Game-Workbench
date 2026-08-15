using System.Text.Json;
using Workbench.App.Leader;
using Workbench.Core.Tasks;
using Workbench.Core.Workers;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.Runtime.Runtime;
using Workbench.Storage.Tasks;
using Workbench.Storage.Workers;

namespace Workbench.App.Worker;

public sealed record WorkerStartRequest(
    Workbench.Core.Projects.Project Project,
    Guid TaskId,
    Guid TaskRevisionId,
    string TaskTitle,
    ExecutionProfile ExecutionProfile,
    string LeaderPrompt,
    AgentSessionId? ReuseWorkerSessionId,
    string WorkerLabel,
    Func<WorkerHandoff, CancellationToken, Task>? OnHandoff = null,
    Guid? ExecutionId = null,
    WorkerExecutionIdentity? ExecutionIdentity = null);

public sealed record WorkerStartResult(bool Succeeded, AgentSession? WorkerSession, string? Error);

public sealed record WorkerSessionRecord(
    Guid ProjectId,
    Guid TaskId,
    string TaskTitle,
    AgentSession Session,
    ExecutionProfile Profile,
    string Label,
    DateTimeOffset LastActiveAt,
    Guid? ExecutionId = null);

public sealed record WorkerHandoff(
    Guid ProjectId,
    Guid TaskId,
    AgentSessionId WorkerSessionId,
    string WorkerLabel,
    AgentSessionStatus Status,
    string Message,
    DateTimeOffset CreatedAt,
    WorkerHandoffKind Kind = WorkerHandoffKind.NeedsLeaderDecision,
    string? ValidationSummary = null,
    Guid? SourceEventId = null,
    Guid TaskRevisionId = default);

public sealed record WorkerRemoval(Guid ProjectId, Guid TaskId, AgentSessionId WorkerSessionId, DateTimeOffset RemovedAt);

public interface IWorkerRoutingStore
{
    Task SaveSessionAsync(WorkerSessionRecord session, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkerSessionRecord>> ListSessionsAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<WorkerSessionRecord?> GetSessionAsync(Guid projectId, Guid taskId, AgentSessionId sessionId, CancellationToken cancellationToken = default);
    Task AppendHandoffAsync(WorkerHandoff handoff, CancellationToken cancellationToken = default);
    Task AppendRemovalAsync(WorkerRemoval removal, CancellationToken cancellationToken = default);
}

public sealed class TaskEventWorkerRoutingStore : IWorkerRoutingStore
{
    private readonly TaskEventRepository _events;
    private readonly WorkerExecutionRepository? _executions;

    public TaskEventWorkerRoutingStore(TaskEventRepository events, WorkerExecutionRepository? executions = null)
    {
        _events = events;
        _executions = executions;
    }

    public Task SaveSessionAsync(WorkerSessionRecord session, CancellationToken cancellationToken = default) =>
        _events.AppendAsync(new StoredTaskEvent(Guid.NewGuid(), session.ProjectId, session.TaskId, null,
            "WorkerSessionStarted", JsonSerializer.Serialize(StoredSession.From(session)), session.LastActiveAt), cancellationToken);

    public async Task<WorkerSessionRecord?> GetSessionAsync(Guid projectId, Guid taskId, AgentSessionId sessionId, CancellationToken cancellationToken = default)
    {
        if (_executions is not null)
        {
            var execution = await _executions.GetByAgentSessionIdAsync(projectId, taskId, sessionId.Value, cancellationToken);
            if (execution is not null) return await ToRecordAsync(execution, cancellationToken);
        }

        return await GetLegacySessionAsync(projectId, taskId, sessionId, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkerSessionRecord>> ListSessionsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var typed = _executions is null
            ? []
            : (await _executions.ListAsync(projectId, cancellationToken)).Where(item => item.AgentSessionId is not null).ToArray();
        var typedSessions = new List<WorkerSessionRecord>(typed.Length);
        foreach (var execution in typed) typedSessions.Add(await ToRecordAsync(execution, cancellationToken));

        var legacy = await ListLegacySessionsAsync(projectId, cancellationToken);
        var typedIds = typedSessions.Select(item => item.Session.Id).ToHashSet();
        return typedSessions.Concat(legacy.Where(item => !typedIds.Contains(item.Session.Id))).ToArray();
    }

    public Task AppendHandoffAsync(WorkerHandoff handoff, CancellationToken cancellationToken = default) =>
        _events.AppendAsync(new StoredTaskEvent(Guid.NewGuid(), handoff.ProjectId, handoff.TaskId, null,
            "WorkerToLeaderHandoff", JsonSerializer.Serialize(handoff), handoff.CreatedAt), cancellationToken);

    public Task AppendRemovalAsync(WorkerRemoval removal, CancellationToken cancellationToken = default) =>
        _events.AppendAsync(new StoredTaskEvent(Guid.NewGuid(), removal.ProjectId, removal.TaskId, null,
            "WorkerRemoved", JsonSerializer.Serialize(removal), removal.RemovedAt), cancellationToken);

    private async Task<WorkerSessionRecord> ToRecordAsync(StoredWorkerExecution execution, CancellationToken cancellationToken)
    {
        var sessionId = Guid.Parse(execution.AgentSessionId!);
        var historical = await GetLegacySessionAsync(execution.ProjectId, execution.TaskId, new AgentSessionId(sessionId), cancellationToken);
        var now = execution.UpdatedAt;
        var accountId = Guid.TryParse(execution.ProviderAccount.AccountId, out var parsedAccountId) ? parsedAccountId : Guid.Empty;
        var session = new AgentSession(new AgentSessionId(sessionId),
            new Workbench.Runtime.Providers.ProviderAccountId(accountId),
            new Workbench.Runtime.Providers.ProviderId(execution.ProviderAccount.ProviderId),
            execution.ExecutionProfile.ModelProfileId, execution.WorkingDirectory ?? execution.WorkerWorktreePath,
            execution.ExternalSessionId, StatusFor(execution.State), execution.CreatedAt, execution.UpdatedAt);
        return new WorkerSessionRecord(execution.ProjectId, execution.TaskId,
            historical?.TaskTitle ?? "Worker execution", session, execution.ExecutionProfile,
            historical?.Label ?? "Worker", now, execution.ExecutionId);
    }

    private async Task<WorkerSessionRecord?> GetLegacySessionAsync(Guid projectId, Guid taskId, AgentSessionId sessionId, CancellationToken cancellationToken)
    {
        var events = await _events.ListAsync(projectId, taskId, 200, cancellationToken);
        if (events.Where(item => item.Type == "WorkerRemoved")
            .Select(TryDeserializeRemoval).Any(item => item?.WorkerSessionId == sessionId)) return null;
        return events.Where(item => item.Type == "WorkerSessionStarted")
            .Select(TryDeserializeSession).LastOrDefault(item => item?.Session.Id == sessionId);
    }

    private async Task<IReadOnlyList<WorkerSessionRecord>> ListLegacySessionsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var started = (await _events.ListForProjectAsync(projectId, "WorkerSessionStarted", 200, cancellationToken))
            .Select(TryDeserializeSession).Where(item => item is not null).Cast<WorkerSessionRecord>().ToArray();
        var removed = (await _events.ListForProjectAsync(projectId, "WorkerRemoved", 200, cancellationToken))
            .Select(TryDeserializeRemoval).Where(item => item is not null).Select(item => item!.WorkerSessionId).ToHashSet();
        var sessions = new List<WorkerSessionRecord>(started.Length);
        foreach (var session in started.Where(item => !removed.Contains(item.Session.Id)))
        {
            var handoff = (await _events.ListAsync(projectId, session.TaskId, 200, cancellationToken))
                .Where(item => item.Type == "WorkerToLeaderHandoff").Select(TryDeserializeHandoff)
                .Where(item => item?.WorkerSessionId == session.Session.Id).OrderByDescending(item => item!.CreatedAt).FirstOrDefault();
            sessions.Add(handoff is null ? session : session with
            {
                Session = session.Session with { Status = handoff.Status, UpdatedAt = handoff.CreatedAt },
                LastActiveAt = handoff.CreatedAt
            });
        }
        return sessions;
    }

    private static WorkerSessionRecord? TryDeserializeSession(StoredTaskEvent item)
    {
        try { return JsonSerializer.Deserialize<StoredSession>(item.Payload)?.ToRecord(); } catch (JsonException) { return null; }
    }
    private static WorkerRemoval? TryDeserializeRemoval(StoredTaskEvent item)
    {
        try { return JsonSerializer.Deserialize<WorkerRemoval>(item.Payload); } catch (JsonException) { return null; }
    }
    private static WorkerHandoff? TryDeserializeHandoff(StoredTaskEvent item)
    {
        try { return JsonSerializer.Deserialize<WorkerHandoff>(item.Payload); } catch (JsonException) { return null; }
    }

    private static AgentSessionStatus StatusFor(WorkerExecutionState state) => state switch
    {
        WorkerExecutionState.Running => AgentSessionStatus.Running,
        WorkerExecutionState.CompletedPendingReview => AgentSessionStatus.Completed,
        WorkerExecutionState.Failed => AgentSessionStatus.Failed,
        WorkerExecutionState.Interrupted => AgentSessionStatus.Interrupted,
        _ => AgentSessionStatus.Ready
    };
}

internal sealed record StoredSession(Guid ProjectId, Guid TaskId, string TaskTitle, Guid SessionId, Guid AccountId, string ProviderId,
    string ModelId, string? WorkingDirectory, string? ExternalSessionId, AgentSessionStatus Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string RecommendedProviderId, string RecommendedAccountId,
    string RecommendedModelId, string RecommendedRuntimeId, string Label, DateTimeOffset LastActiveAt)
{
    public static StoredSession From(WorkerSessionRecord value) => new(value.ProjectId, value.TaskId, value.TaskTitle, value.Session.Id.Value,
        value.Session.AccountId.Value, value.Session.ProviderId.Value, value.Session.ModelId, value.Session.WorkingDirectory,
        value.Session.ExternalSessionId, value.Session.Status, value.Session.CreatedAt, value.Session.UpdatedAt,
        value.Profile.ProviderId, value.Profile.ProviderAccountId, value.Profile.ModelProfileId, value.Profile.AgentRuntimeId,
        value.Label, value.LastActiveAt);
    public WorkerSessionRecord ToRecord() => new(ProjectId, TaskId, TaskTitle,
        new AgentSession(new AgentSessionId(SessionId), new Workbench.Runtime.Providers.ProviderAccountId(AccountId),
            new Workbench.Runtime.Providers.ProviderId(ProviderId), ModelId, WorkingDirectory, ExternalSessionId, Status, CreatedAt, UpdatedAt),
        ExecutionProfile.Create(RecommendedProviderId, RecommendedAccountId, RecommendedModelId, RecommendedRuntimeId), Label, LastActiveAt);
}

public sealed class WorkerSessionRouter(
    AgentRuntimeRegistry runtimes,
    IWorkerRoutingStore store,
    TimeProvider time,
    AssignmentReviewStateRepository? assignments = null,
    ILeaderReviewOrchestrator? reviews = null,
    WorkerExecutionRepository? executions = null)
{
    public async Task<WorkerStartResult> StartAsync(WorkerStartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LeaderPrompt);
        var runtime = runtimes.GetByAccount(new Workbench.Runtime.Providers.ProviderAccountId(Guid.Parse(request.ExecutionProfile.ProviderAccountId)));
        var created = request.ReuseWorkerSessionId is null;
        var typed = executions is not null && request.ExecutionId.HasValue && request.ExecutionIdentity is not null;
        if (typed && request.ExecutionIdentity!.TaskId != request.TaskId) throw new InvalidOperationException("Execution task mismatch.");
        var executionId = typed ? request.ExecutionId : null;
        AgentSession session;

        if (created)
        {
            if (typed)
            {
                var identity = request.ExecutionIdentity!;
                await executions!.CreateAsync(new StoredWorkerExecution(
                    request.ExecutionId!.Value, request.Project.Id, request.TaskId, identity.ExecutionStartRevision,
                    identity.CurrentAcknowledgedRevision, identity.BaseCommit, identity.TargetBranch, identity.ProviderAccount,
                    identity.ExecutionProfile, identity.WorkerBranch, identity.WorkerWorktreePath, WorkerExecutionState.RuntimeStarting,
                    null, null, null, time.GetUtcNow(), time.GetUtcNow()), cancellationToken);
            }

            AgentSession? createdSession = null;
            try
            {
                createdSession = await runtime.CreateSessionAsync(new CreateAgentSessionRequest(runtime.Account.Id, request.ExecutionProfile.ModelProfileId, request.Project.RootPath), cancellationToken);
                session = createdSession;
            }
            catch
            {
                if (typed)
                {
                    try { await executions!.UpdateStateAsync(request.Project.Id, request.TaskId, request.ExecutionId!.Value, WorkerExecutionState.Failed, CancellationToken.None); } catch { }
                }
                throw;
            }

            try
            {
                if (typed)
                {
                    await executions!.PersistSessionIdentityAsync(request.Project.Id, request.TaskId, request.ExecutionId!.Value,
                        session.Id.Value.ToString(), session.ExternalSessionId, session.WorkingDirectory ?? request.Project.RootPath, cancellationToken);
                    await executions.UpdateStateAsync(request.Project.Id, request.TaskId, request.ExecutionId.Value, WorkerExecutionState.Running, cancellationToken);
                }
                await store.SaveSessionAsync(new WorkerSessionRecord(request.Project.Id, request.TaskId, request.TaskTitle, session,
                    request.ExecutionProfile, request.WorkerLabel, time.GetUtcNow(), executionId), cancellationToken);
            }
            catch (Exception exception)
            {
                if (typed)
                {
                    try { await executions!.UpdateStateAsync(request.Project.Id, request.TaskId, request.ExecutionId!.Value, WorkerExecutionState.Failed, CancellationToken.None); } catch { }
                }
                try { if (createdSession is not null) await runtime.StopAsync(createdSession, CancellationToken.None); } catch { }
                return new WorkerStartResult(false, null, exception.Message);
            }
        }
        else
        {
            var reuseSessionId = request.ReuseWorkerSessionId.GetValueOrDefault();
            var persisted = await store.GetSessionAsync(request.Project.Id, request.TaskId, reuseSessionId, cancellationToken);
            if (persisted is null || persisted.Session.AccountId != runtime.Account.Id)
                return new WorkerStartResult(false, null, "The requested Worker session is not available for this task and account.");
            session = persisted.Session;
            executionId ??= persisted.ExecutionId;
            if (executions is not null && executionId.HasValue)
            {
                await executions.UpdateStateAsync(request.Project.Id, request.TaskId, executionId.Value, WorkerExecutionState.Running, cancellationToken);
            }
        }

        if (created && assignments is not null)
        {
            var ready = await assignments.TryTransitionAsync(request.Project.Id, request.TaskId, TaskLifecycleStatus.Draft, TaskLifecycleStatus.ReadyToStart,
                Guid.NewGuid(), "AssignmentReadyToStart", "{}", time.GetUtcNow(), cancellationToken);
            if (ready is not AssignmentStateTransitionResult.Applied and not AssignmentStateTransitionResult.Conflict)
            {
                await runtime.StopAsync(session, CancellationToken.None);
                return new WorkerStartResult(false, null, "The Assignment was not ready to start.");
            }
            var started = await assignments.TryTransitionAsync(request.Project.Id, request.TaskId, TaskLifecycleStatus.ReadyToStart, TaskLifecycleStatus.Working,
                Guid.NewGuid(), "WorkerAssignmentStarted", JsonSerializer.Serialize(new { WorkerSessionId = session.Id.Value }), time.GetUtcNow(), cancellationToken);
            if (started != AssignmentStateTransitionResult.Applied)
            {
                await runtime.StopAsync(session, CancellationToken.None);
                return new WorkerStartResult(false, null, "The Assignment was not ready to start.");
            }
        }

        await foreach (var item in runtime.SendAsync(session, new AgentRequest(request.LeaderPrompt), cancellationToken))
        {
            if (item is not AgentTurnCompleted completed || string.IsNullOrWhiteSpace(completed.Result.FinalText)) continue;
            var isTypedHandoff = WorkerHandoffPayloadParser.TryParse(completed.Result.FinalText, out var payload);
            var eventId = Guid.NewGuid();
            if (isTypedHandoff && payload!.Kind == WorkerHandoffKind.FinalReport && assignments is not null)
            {
                var transition = await assignments.TryTransitionAsync(request.Project.Id, request.TaskId, TaskLifecycleStatus.Working, TaskLifecycleStatus.Reviewing,
                    eventId, "WorkerFinalReportReceived", JsonSerializer.Serialize(new { WorkerSessionId = session.Id.Value, payload.Message, payload.ValidationSummary }), time.GetUtcNow(), cancellationToken);
                if (transition != AssignmentStateTransitionResult.Applied) continue;
            }
            else if (isTypedHandoff && payload!.Kind == WorkerHandoffKind.NeedsLeaderDecision && assignments is not null)
            {
                var transition = await assignments.TryTransitionAsync(request.Project.Id, request.TaskId, TaskLifecycleStatus.Working, TaskLifecycleStatus.NeedsLeaderDecision,
                    eventId, "WorkerNeedsLeaderDecisionReceived", JsonSerializer.Serialize(new { WorkerSessionId = session.Id.Value, payload.Message }), time.GetUtcNow(), cancellationToken);
                if (transition != AssignmentStateTransitionResult.Applied) continue;
            }

            var handoff = new WorkerHandoff(request.Project.Id, request.TaskId, session.Id, request.WorkerLabel, completed.Result.FinalStatus,
                isTypedHandoff ? payload!.Message : completed.Result.FinalText!, time.GetUtcNow(),
                isTypedHandoff ? payload!.Kind : WorkerHandoffKind.NeedsLeaderDecision,
                isTypedHandoff ? payload!.ValidationSummary : null, eventId, request.TaskRevisionId);
            if (executions is not null && executionId.HasValue)
            {
                await executions.UpdateStateAsync(request.Project.Id, request.TaskId, executionId.Value,
                    isTypedHandoff && payload!.Kind == WorkerHandoffKind.FinalReport ? WorkerExecutionState.CompletedPendingReview : WorkerExecutionState.Blocked,
                    cancellationToken);
            }
            await store.AppendHandoffAsync(handoff, cancellationToken);
            if (isTypedHandoff && payload!.Kind == WorkerHandoffKind.FinalReport && reviews is not null)
                await reviews.TryReviewAsync(request.Project.Id, request.TaskId, eventId, cancellationToken);
            if (request.OnHandoff is not null)
            {
                try { await request.OnHandoff(handoff, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { }
            }
        }
        return new WorkerStartResult(true, session, null);
    }
}
