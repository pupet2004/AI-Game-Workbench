using System.Text.Json;
using System.Collections.Concurrent;
using Workbench.App.Leader;
using Workbench.App.Skills;
using Workbench.Core.Tasks;
using Workbench.Core.Workers;
using Workbench.Core.Continuity;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.Runtime.Runtime;
using Workbench.Storage.Tasks;
using Workbench.Storage.Workers;
using Workbench.App.AgentHost;
using Workbench.App.Continuity;

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
    WorkerExecutionIdentity? ExecutionIdentity = null,
    bool WaitForCompletion = true,
    AgentIntentSource PromptSource = AgentIntentSource.Leader,
    AssignmentRef? B1AssignmentRef = null,
    RevisionRef? B1AssignmentRevisionRef = null,
    AttemptRef? B1AttemptRef = null,
    SessionBindingRef? B1SessionBindingRef = null,
    LogicalActorRef? B1LogicalActorRef = null,
    UserPrincipalRef? B1OperatorRef = null,
    Func<StoredCanonicalWorkerCompletion, CancellationToken, Task>? OnCanonicalCompletion = null);

public sealed record WorkerStartResult(bool Succeeded, AgentSession? WorkerSession, string? Error);

public sealed record WorkerSessionRecord(
    Guid ProjectId,
    Guid TaskId,
    string TaskTitle,
    AgentSession Session,
    ExecutionProfile Profile,
    string Label,
    DateTimeOffset LastActiveAt,
    Guid? ExecutionId = null,
    Guid TaskRevisionId = default);

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

public sealed record WorkerStatusOverride(
    Guid ProjectId,
    Guid TaskId,
    AgentSessionId WorkerSessionId,
    AgentSessionStatus Status,
    DateTimeOffset ChangedAt,
    string Reason);

public sealed record WorkerProgressStep(string Id, AgentPlanStepStatus Status);

public sealed record WorkerProgressSnapshot(
    Guid ProjectId,
    Guid TaskId,
    AgentSessionId WorkerSessionId,
    IReadOnlyList<WorkerProgressStep> Steps,
    DateTimeOffset UpdatedAt);

public sealed record WorkerSessionSurfaceLease(
    Guid ProjectId,
    Guid TaskId,
    AgentSessionId WorkerSessionId,
    Guid ContinuationAttemptId,
    int? ProcessId,
    bool Active,
    DateTimeOffset ChangedAt,
    WorkerSessionSurfaceState State = WorkerSessionSurfaceState.WorkbenchOwned)
{
    public bool IsActive => Active;
}

public enum WorkerSessionSurfaceState
{
    Preparing,
    CliOwned,
    Reacquiring,
    WorkbenchOwned,
    RecoveryRequired
}

public interface IWorkerRoutingStore
{
    Task SaveSessionAsync(WorkerSessionRecord session, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkerSessionRecord>> ListSessionsAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<WorkerSessionRecord?> GetSessionAsync(Guid projectId, Guid taskId, AgentSessionId sessionId, CancellationToken cancellationToken = default);
    Task AppendHandoffAsync(WorkerHandoff handoff, CancellationToken cancellationToken = default);
    Task<WorkerHandoff?> GetLatestHandoffAsync(Guid projectId, Guid taskId, AgentSessionId sessionId, CancellationToken cancellationToken = default) => Task.FromResult<WorkerHandoff?>(null);
    Task AppendRemovalAsync(WorkerRemoval removal, CancellationToken cancellationToken = default);
    Task OverrideStatusAsync(WorkerStatusOverride status, CancellationToken cancellationToken = default);
    Task AppendProgressAsync(WorkerProgressSnapshot progress, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task<WorkerProgressSnapshot?> GetProgressAsync(Guid projectId, Guid taskId, AgentSessionId sessionId, CancellationToken cancellationToken = default) => Task.FromResult<WorkerProgressSnapshot?>(null);
    Task AppendSurfaceLeaseAsync(WorkerSessionSurfaceLease lease, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task<WorkerSessionSurfaceLease?> GetActiveSurfaceLeaseAsync(Guid projectId, Guid taskId, AgentSessionId sessionId, CancellationToken cancellationToken = default) => Task.FromResult<WorkerSessionSurfaceLease?>(null);
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

    public Task AppendHandoffAsync(WorkerHandoff handoff, CancellationToken cancellationToken = default)
    {
        if (handoff.Kind == WorkerHandoffKind.FinalReport &&
            (handoff.SourceEventId is null || handoff.SourceEventId == Guid.Empty))
        {
            throw new ArgumentException("A FinalReport handoff requires a canonical source event.", nameof(handoff));
        }

        var payload = handoff.Kind == WorkerHandoffKind.FinalReport
            ? JsonSerializer.Serialize(StoredHandoff.From(handoff))
            : JsonSerializer.Serialize(handoff);
        return _events.AppendAsync(new StoredTaskEvent(Guid.NewGuid(), handoff.ProjectId, handoff.TaskId, null,
            "WorkerToLeaderHandoff", payload, handoff.CreatedAt), cancellationToken);
    }

    public async Task<WorkerHandoff?> GetLatestHandoffAsync(Guid projectId, Guid taskId, AgentSessionId sessionId, CancellationToken cancellationToken = default)
    {
        var events = await _events.ListAsync(projectId, taskId, 200, cancellationToken);
        return events.Where(item => item.Type == "WorkerToLeaderHandoff")
            .Select(TryDeserializeHandoff)
            .Where(item => item?.WorkerSessionId == sessionId)
            .OrderByDescending(item => item!.CreatedAt)
            .FirstOrDefault();
    }

    public Task AppendRemovalAsync(WorkerRemoval removal, CancellationToken cancellationToken = default) =>
        _events.AppendAsync(new StoredTaskEvent(Guid.NewGuid(), removal.ProjectId, removal.TaskId, null,
            "WorkerRemoved", JsonSerializer.Serialize(removal), removal.RemovedAt), cancellationToken);

    public Task AppendSurfaceLeaseAsync(WorkerSessionSurfaceLease lease, CancellationToken cancellationToken = default) =>
        _events.AppendAsync(new StoredTaskEvent(Guid.NewGuid(), lease.ProjectId, lease.TaskId, null,
            "WorkerSessionSurfaceLease", JsonSerializer.Serialize(lease), lease.ChangedAt), cancellationToken);

    public Task AppendProgressAsync(WorkerProgressSnapshot progress, CancellationToken cancellationToken = default) =>
        _events.AppendAsync(new StoredTaskEvent(Guid.NewGuid(), progress.ProjectId, progress.TaskId, null,
            "WorkerProgressUpdated", JsonSerializer.Serialize(progress), progress.UpdatedAt), cancellationToken);

    public async Task<WorkerProgressSnapshot?> GetProgressAsync(Guid projectId, Guid taskId, AgentSessionId sessionId, CancellationToken cancellationToken = default)
    {
        var events = await _events.ListAsync(projectId, taskId, 500, cancellationToken);
        return events.Where(item => item.Type == "WorkerProgressUpdated")
            .Select(TryDeserializeProgress)
            .Where(item => item is not null && item.WorkerSessionId == sessionId)
            .Select(item => item!)
            .OrderBy(item => item.UpdatedAt)
            .LastOrDefault();
    }

    public async Task<WorkerSessionSurfaceLease?> GetActiveSurfaceLeaseAsync(Guid projectId, Guid taskId, AgentSessionId sessionId, CancellationToken cancellationToken = default)
    {
        var events = await _events.ListAsync(projectId, taskId, 200, cancellationToken);
        var latest = events.Where(item => item.Type == "WorkerSessionSurfaceLease")
            .Select(item => TryDeserializeSurfaceLease(item))
            .Where(item => item is not null && item.WorkerSessionId == sessionId)
            .Select(item => item!)
            .LastOrDefault();
        return latest is { Active: true } && latest.IsActive ? latest : null;
    }

    public async Task OverrideStatusAsync(WorkerStatusOverride status, CancellationToken cancellationToken = default)
    {
        if (_executions is not null)
        {
            var execution = await _executions.GetByAgentSessionIdAsync(status.ProjectId, status.TaskId, status.WorkerSessionId.Value, cancellationToken);
            if (execution is not null)
            {
                var nextState = status.Status switch
                {
                    AgentSessionStatus.Completed => WorkerExecutionState.CompletedPendingReview,
                    AgentSessionStatus.Interrupted => WorkerExecutionState.Interrupted,
                    AgentSessionStatus.Failed => WorkerExecutionState.Failed,
                    _ => throw new ArgumentException("Only terminal Worker statuses can be overridden.", nameof(status))
                };
                await _executions.UpdateStateAsync(status.ProjectId, status.TaskId, execution.ExecutionId, nextState, cancellationToken);
            }
        }

        await _events.AppendAsync(new StoredTaskEvent(Guid.NewGuid(), status.ProjectId, status.TaskId, null,
            "WorkerStatusOverride", JsonSerializer.Serialize(status), status.ChangedAt), cancellationToken);
    }

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
            historical?.Label ?? "Worker", now, execution.ExecutionId,
            execution.CurrentAcknowledgedRevision.RevisionId);
    }

    private async Task<WorkerSessionRecord?> GetLegacySessionAsync(Guid projectId, Guid taskId, AgentSessionId sessionId, CancellationToken cancellationToken)
    {
        var events = await _events.ListAsync(projectId, taskId, 200, cancellationToken);
        if (events.Where(item => item.Type == "WorkerRemoved")
            .Select(TryDeserializeRemoval).Any(item => item?.WorkerSessionId == sessionId)) return null;
        var session = events.Where(item => item.Type == "WorkerSessionStarted")
            .Select(TryDeserializeSession).LastOrDefault(item => item?.Session.Id == sessionId);
        if (session is null) return null;

        var handoff = events.Where(item => item.Type == "WorkerToLeaderHandoff").Select(TryDeserializeHandoff)
            .Where(item => item?.WorkerSessionId == sessionId).OrderByDescending(item => item!.CreatedAt).FirstOrDefault();
        var statusOverride = events.Where(item => item.Type == "WorkerStatusOverride").Select(TryDeserializeStatusOverride)
            .Where(item => item?.WorkerSessionId == sessionId).OrderByDescending(item => item!.ChangedAt).FirstOrDefault();
        if (statusOverride is { } overrideValue && (handoff is null || overrideValue.ChangedAt > handoff.CreatedAt))
        {
            return session with
            {
                Session = session.Session with { Status = overrideValue.Status, UpdatedAt = overrideValue.ChangedAt },
                LastActiveAt = overrideValue.ChangedAt
            };
        }

        return handoff is null ? session : session with
        {
            Session = session.Session with { Status = handoff.Status, UpdatedAt = handoff.CreatedAt },
            LastActiveAt = handoff.CreatedAt
        };
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
            var taskEvents = await _events.ListAsync(projectId, session.TaskId, 200, cancellationToken);
            var handoff = taskEvents.Where(item => item.Type == "WorkerToLeaderHandoff").Select(TryDeserializeHandoff)
                .Where(item => item?.WorkerSessionId == session.Session.Id).OrderByDescending(item => item!.CreatedAt).FirstOrDefault();
            var statusOverride = taskEvents.Where(item => item.Type == "WorkerStatusOverride").Select(TryDeserializeStatusOverride)
                .Where(item => item?.WorkerSessionId == session.Session.Id).OrderByDescending(item => item!.ChangedAt).FirstOrDefault();
            if (statusOverride is { } overrideValue && (handoff is null || overrideValue.ChangedAt > handoff.CreatedAt))
            {
                sessions.Add(session with
                {
                    Session = session.Session with { Status = overrideValue.Status, UpdatedAt = overrideValue.ChangedAt },
                    LastActiveAt = overrideValue.ChangedAt
                });
            }
            else
            {
                sessions.Add(handoff is null ? session : session with
                {
                    Session = session.Session with { Status = handoff.Status, UpdatedAt = handoff.CreatedAt },
                    LastActiveAt = handoff.CreatedAt
                });
            }
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
        try
        {
            var parsed = JsonSerializer.Deserialize<HandoffEventPayload>(item.Payload);
            if (parsed is null || parsed.ProjectId == Guid.Empty || parsed.TaskId == Guid.Empty ||
                !TryReadGuid(parsed.WorkerSessionId, out var workerSessionId)) return null;
            return new WorkerHandoff(parsed.ProjectId, parsed.TaskId, new AgentSessionId(workerSessionId),
                parsed.WorkerLabel ?? string.Empty, parsed.Status, parsed.Message ?? string.Empty, parsed.CreatedAt,
                parsed.Kind, parsed.ValidationSummary, parsed.SourceEventId, parsed.TaskRevisionId);
        }
        catch (JsonException) { return null; }
    }

    private static WorkerStatusOverride? TryDeserializeStatusOverride(StoredTaskEvent item)
    {
        try { return JsonSerializer.Deserialize<WorkerStatusOverride>(item.Payload); } catch (JsonException) { return null; }
    }

    private static WorkerSessionSurfaceLease? TryDeserializeSurfaceLease(StoredTaskEvent item)
    {
        try { return JsonSerializer.Deserialize<WorkerSessionSurfaceLease>(item.Payload); } catch (JsonException) { return null; }
    }

    private static WorkerProgressSnapshot? TryDeserializeProgress(StoredTaskEvent item)
    {
        try { return JsonSerializer.Deserialize<WorkerProgressSnapshot>(item.Payload); } catch (JsonException) { return null; }
    }

    private static bool TryReadGuid(JsonElement value, out Guid result)
    {
        if (value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out result)) return true;
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("Value", out var nested) &&
            nested.ValueKind == JsonValueKind.String && Guid.TryParse(nested.GetString(), out result)) return true;
        result = Guid.Empty;
        return false;
    }

    private static AgentSessionStatus StatusFor(WorkerExecutionState state) => state switch
    {
        WorkerExecutionState.Running => AgentSessionStatus.Running,
        WorkerExecutionState.Blocked => AgentSessionStatus.WaitingApproval,
        WorkerExecutionState.CompletedPendingReview => AgentSessionStatus.Completed,
        WorkerExecutionState.Failed => AgentSessionStatus.Failed,
        WorkerExecutionState.Interrupted => AgentSessionStatus.Interrupted,
        _ => AgentSessionStatus.Ready
    };

    private sealed record HandoffEventPayload(
        Guid ProjectId,
        Guid TaskId,
        JsonElement WorkerSessionId,
        string? WorkerLabel,
        AgentSessionStatus Status,
        string? Message,
        DateTimeOffset CreatedAt,
        WorkerHandoffKind Kind,
        string? ValidationSummary,
        Guid? SourceEventId,
        Guid TaskRevisionId);
}

internal sealed record StoredSession(Guid ProjectId, Guid TaskId, string TaskTitle, Guid SessionId, Guid AccountId, string ProviderId,
    string ModelId, string? WorkingDirectory, string? ExternalSessionId, AgentSessionStatus Status,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string RecommendedProviderId, string RecommendedAccountId,
    string RecommendedModelId, string RecommendedRuntimeId, string Label, DateTimeOffset LastActiveAt,
    Guid TaskRevisionId = default)
{
    public static StoredSession From(WorkerSessionRecord value) => new(value.ProjectId, value.TaskId, value.TaskTitle, value.Session.Id.Value,
        value.Session.AccountId.Value, value.Session.ProviderId.Value, value.Session.ModelId, value.Session.WorkingDirectory,
        value.Session.ExternalSessionId, value.Session.Status, value.Session.CreatedAt, value.Session.UpdatedAt,
        value.Profile.ProviderId, value.Profile.ProviderAccountId, value.Profile.ModelProfileId, value.Profile.AgentRuntimeId,
        value.Label, value.LastActiveAt, value.TaskRevisionId);
    public WorkerSessionRecord ToRecord() => new(ProjectId, TaskId, TaskTitle,
        new AgentSession(new AgentSessionId(SessionId), new Workbench.Runtime.Providers.ProviderAccountId(AccountId),
            new Workbench.Runtime.Providers.ProviderId(ProviderId), ModelId, WorkingDirectory, ExternalSessionId, Status, CreatedAt, UpdatedAt),
        ExecutionProfile.Create(RecommendedProviderId, RecommendedAccountId, RecommendedModelId, RecommendedRuntimeId), Label, LastActiveAt,
        TaskRevisionId: TaskRevisionId);
}

internal sealed record StoredHandoff(
    Guid ProjectId,
    Guid TaskId,
    Guid WorkerSessionId,
    string WorkerLabel,
    AgentSessionStatus Status,
    DateTimeOffset CreatedAt,
    WorkerHandoffKind Kind,
    Guid? SourceEventId,
    Guid TaskRevisionId)
{
    public static StoredHandoff From(WorkerHandoff value) => new(value.ProjectId, value.TaskId, value.WorkerSessionId.Value,
        value.WorkerLabel, value.Status, value.CreatedAt, value.Kind, value.SourceEventId, value.TaskRevisionId);
}

public sealed class WorkerSessionRouter(
    AgentRuntimeRegistry runtimes,
    IWorkerRoutingStore store,
    TimeProvider time,
    AssignmentReviewStateRepository? assignments = null,
    ILeaderReviewOrchestrator? reviews = null,
    WorkerExecutionRepository? executions = null,
    IAgentHost? agentHost = null,
    TaskRevisionRepository? taskRevisions = null,
    TaskEventRepository? taskEvents = null,
    B1WorkerExecutionBridgeService? b1WorkerExecutionBridge = null,
    B1NonAuthoritativeCommandService? b1RoutingCommands = null,
    WorkerCompletionSummaryConsumer? completionSummaryConsumer = null,
    CanonicalWorkerCompletionBridgeService? canonicalWorkerCompletionBridge = null,
    CanonicalWorkerLaunchService? canonicalWorkerLaunch = null)
{
    private readonly IAgentHost _agentHost = agentHost ?? new InProcessAgentHost(runtimes);
    private readonly ConcurrentDictionary<AgentSessionId, Task> _intentTails = new();
    private readonly WorkerCompletionVerifier _completionVerifier = new();
    private readonly TaskEventRepository? _taskEvents = taskEvents;
    private readonly B1WorkerExecutionBridgeService? _b1WorkerExecutionBridge = b1WorkerExecutionBridge;
    private readonly B1NonAuthoritativeCommandService? _b1RoutingCommands = b1RoutingCommands;
    private readonly CanonicalWorkerCompletionBridgeService? _canonicalWorkerCompletionBridge = canonicalWorkerCompletionBridge;
    private readonly CanonicalWorkerLaunchService? _canonicalWorkerLaunch = canonicalWorkerLaunch;

    public async Task<int> ReconcileCompletedAssignmentsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        if (assignments is null || executions is null)
            return 0;

        var completedByTask = (await executions.ListAsync(projectId, cancellationToken))
            .Where(item => item.State == WorkerExecutionState.CompletedPendingReview)
            .GroupBy(item => item.TaskId)
            .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
            .ToArray();
        var repaired = 0;
        foreach (var execution in completedByTask)
        {
            var state = await assignments.GetRecoveryStateAsync(projectId, execution.TaskId, cancellationToken);
            if (state?.Task.Status != TaskLifecycleStatus.Working)
                continue;

            var transition = await assignments.TryTransitionAsync(
                projectId,
                execution.TaskId,
                TaskLifecycleStatus.Working,
                TaskLifecycleStatus.Reviewing,
                Guid.NewGuid(),
                "WorkerFinalReportReceived",
                JsonSerializer.Serialize(new
                {
                    execution.ExecutionId,
                    execution.AgentSessionId,
                    RecoveredFromCompletedExecution = true
                }),
                time.GetUtcNow(),
                cancellationToken);
            if (transition == AssignmentStateTransitionResult.Applied)
                repaired++;
        }

        return repaired;
    }

    /// <summary>
    /// Routes a follow-up direction to an already hosted Worker session.
    /// Active turns use the provider's steer channel when available; all
    /// other directions are serialized as continuations behind the current
    /// turn. The caller never needs to open or own a CLI process.
    /// </summary>
    public async Task<WorkerStartResult> SendIntentAsync(
        Workbench.Core.Projects.Project project,
        Guid taskId,
        AgentSessionId workerSessionId,
        string text,
        AgentIntentSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var persisted = await store.GetSessionAsync(project.Id, taskId, workerSessionId, cancellationToken).ConfigureAwait(false);
        if (persisted is null)
            return new WorkerStartResult(false, null, "The requested Worker session is not available.");

        var runtime = runtimes.GetByAccount(persisted.Session.AccountId);
        var snapshot = _agentHost.Attach(persisted.Session);
        if (snapshot.HasActiveTurn && runtime.Capabilities.HasFlag(AgentCapability.Steer))
        {
            try
            {
                await _agentHost.SteerAsync(
                    persisted.Session,
                    new HostedAgentIntent(source, text),
                    cancellationToken).ConfigureAwait(false);
                return new WorkerStartResult(true, persisted.Session, null);
            }
            catch (NotSupportedException)
            {
                // Some runtimes advertise capabilities optimistically. Fall
                // through to a serialized continuation instead of dropping
                // the direction.
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("no active turn", StringComparison.OrdinalIgnoreCase))
            {
                // The turn ended between Attach and SteerAsync. Queue the
                // direction against the same session.
            }
        }

        var request = new WorkerStartRequest(
            project,
            persisted.TaskId,
            persisted.TaskRevisionId,
            persisted.TaskTitle,
            persisted.Profile,
            text,
            persisted.Session.Id,
            persisted.Label,
            WaitForCompletion: true,
            PromptSource: source,
            ExecutionId: persisted.ExecutionId);

        QueueContinuation(persisted.Session.Id, request);
        return new WorkerStartResult(true, persisted.Session, null);
    }

    private void QueueContinuation(AgentSessionId sessionId, WorkerStartRequest request)
    {
        while (true)
        {
            var prior = _intentTails.GetOrAdd(sessionId, Task.CompletedTask);
            var next = prior.ContinueWith(
                    _ => StartAsync(request, CancellationToken.None),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default)
                .Unwrap();
            if (_intentTails.TryUpdate(sessionId, next, prior))
            {
                _ = next.ContinueWith(
                    completed => _ = completed.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
                return;
            }
        }
    }

    public async Task<WorkerStartResult> StartAsync(WorkerStartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LeaderPrompt);
        IAgentRuntime runtime;
        try
        {
            runtime = runtimes.GetByAccount(new Workbench.Runtime.Providers.ProviderAccountId(Guid.Parse(request.ExecutionProfile.ProviderAccountId)));
        }
        catch (Exception exception) when (exception is KeyNotFoundException or FormatException)
        {
            return new WorkerStartResult(false, null, "Worker runtime is not connected. Reconnect the Agent runtime and try again.");
        }
        TaskRevision? effectiveRevision = null;
        if (taskRevisions is not null)
        {
            effectiveRevision = (await taskRevisions.ListAsync(request.Project.Id, request.TaskId, cancellationToken))
                .SingleOrDefault(item => item.Id == request.TaskRevisionId);
            if (effectiveRevision is null)
                return new WorkerStartResult(false, null, "The requested TaskRevision is not available for this project and task.");
        }

        // Legacy callers may know only the Task and typed execution identity.
        // Resolve the current Project World assignment here so their Worker
        // completion enters the canonical governance path as well.
        if (_canonicalWorkerLaunch is not null &&
            effectiveRevision is not null &&
            request.ExecutionId is not null &&
            request.ExecutionIdentity is not null &&
            request.B1AssignmentRef is null &&
            request.B1AssignmentRevisionRef is null &&
            request.B1AttemptRef is null)
        {
            var canonical = await _canonicalWorkerLaunch.PrepareAsync(
                request.Project.Id,
                effectiveRevision,
                cancellationToken);
            if (canonical is not null)
            {
                request = request with
                {
                    B1AssignmentRef = canonical.AssignmentRef,
                    B1AssignmentRevisionRef = canonical.AssignmentRevisionRef,
                    B1AttemptRef = canonical.AttemptRef,
                    B1SessionBindingRef = canonical.SessionBindingRef,
                    B1LogicalActorRef = canonical.LogicalActorRef,
                    B1OperatorRef = canonical.OperatorRef
                };
            }
        }

        var created = request.ReuseWorkerSessionId is null;
        var typed = executions is not null && request.ExecutionId.HasValue && request.ExecutionIdentity is not null;
        var linked = request.B1AssignmentRef.HasValue || request.B1AttemptRef.HasValue || request.B1AssignmentRevisionRef.HasValue;
        if (linked && (!typed || _b1WorkerExecutionBridge is null || request.B1AssignmentRef is null || request.B1AssignmentRevisionRef is null || request.B1AttemptRef is null))
            return new WorkerStartResult(false, null, "A B1-linked Worker start requires Assignment, AssignmentRevision, Attempt, and typed execution identity.");
        if (typed && request.ExecutionIdentity!.TaskId != request.TaskId) throw new InvalidOperationException("Execution task mismatch.");
        var executionId = typed ? request.ExecutionId : null;
        AgentSession session;

        if (created)
        {
            if (typed)
            {
                var identity = request.ExecutionIdentity!;
                // Capture the execution-start filesystem state before the provider
                // session can write. A missing baseline remains explicitly
                // unverifiable; it must never turn into a false scope pass.
                var workspaceBaseline = await WorkspaceSnapshot.CaptureAsync(identity.WorkerWorktreePath, cancellationToken);
                try
                {
                    await executions!.CreateAsync(new StoredWorkerExecution(
                        request.ExecutionId!.Value, request.Project.Id, request.TaskId, identity.ExecutionStartRevision,
                        identity.CurrentAcknowledgedRevision, identity.BaseCommit, identity.TargetBranch, identity.ProviderAccount,
                        identity.ExecutionProfile, identity.WorkerBranch, identity.WorkerWorktreePath, WorkerExecutionState.RuntimeStarting,
                        null, null, null, time.GetUtcNow(), time.GetUtcNow(), workspaceBaseline?.Serialize()), cancellationToken);
                    if (linked)
                    {
                        await _b1WorkerExecutionBridge!.LinkWorkerTaskAsync(
                            new ProjectRef(request.Project.Id), request.B1AssignmentRef!.Value,
                            request.B1AssignmentRevisionRef!.Value, request.TaskId, request.TaskRevisionId, cancellationToken);
                        await _b1WorkerExecutionBridge.LinkWorkerExecutionAsync(
                            new ProjectRef(request.Project.Id), request.B1AttemptRef!.Value,
                            request.ExecutionId!.Value, B1WorkerExecutionRelationKind.Initial, cancellationToken);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    return new WorkerStartResult(false, null, exception.Message);
                }
            }

            AgentSession? createdSession = null;
            try
            {
                createdSession = await _agentHost.CreateSessionAsync(new CreateAgentSessionRequest(runtime.Account.Id, request.ExecutionProfile.ModelProfileId, request.Project.RootPath), cancellationToken);
                session = createdSession;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (typed)
                {
                    try { await executions!.UpdateStateAsync(request.Project.Id, request.TaskId, request.ExecutionId!.Value, WorkerExecutionState.Failed, CancellationToken.None); } catch { }
                }
                return new WorkerStartResult(false, null, $"The Worker Agent session could not be started: {exception.Message}");
            }

            try
            {
                if (typed)
                {
                    await executions!.PersistSessionIdentityAsync(request.Project.Id, request.TaskId, request.ExecutionId!.Value,
                        session.Id.Value.ToString(), session.ExternalSessionId, session.WorkingDirectory ?? request.Project.RootPath, cancellationToken);
                    if (linked && request.B1SessionBindingRef is null && _b1RoutingCommands is not null && request.B1LogicalActorRef is { } actor && request.B1OperatorRef is { } operatorRef)
                    {
                        var external = session.ExternalSessionId ?? session.Id.Value.ToString("N");
                        var binding = await _b1RoutingCommands.CreateSessionBindingAsync(new CreateSessionBindingCommand(
                            new ProjectRef(request.Project.Id), operatorRef,
                            new SessionBinding(new SessionBindingRef(Guid.NewGuid()), request.B1AttemptRef!.Value, actor,
                                new ExternalSessionRef($"{runtime.Provider.Id.Value}:session/{external}"), time.GetUtcNow())), cancellationToken);
                        request = request with { B1SessionBindingRef = binding.SessionBindingRef };
                    }
                    if (linked && request.B1SessionBindingRef is { } bindingRef)
                    {
                        var external = session.ExternalSessionId ?? session.Id.Value.ToString("N");
                        await _b1WorkerExecutionBridge!.LinkAgentSessionAsync(new B1WorkerSessionLink(
                            new ProjectRef(request.Project.Id), bindingRef, request.ExecutionId!.Value, session.Id.Value,
                            new ExternalSessionRef($"{runtime.Provider.Id.Value}:session/{external}"), runtime.Provider.Id.Value,
                            runtime.Account.Id.Value.ToString(), session.ModelId, time.GetUtcNow()), cancellationToken);
                    }
                    await executions.UpdateStateAsync(request.Project.Id, request.TaskId, request.ExecutionId.Value, WorkerExecutionState.Running, cancellationToken);
                }
                await store.SaveSessionAsync(new WorkerSessionRecord(request.Project.Id, request.TaskId, request.TaskTitle, session,
                    request.ExecutionProfile, request.WorkerLabel, time.GetUtcNow(), executionId, request.TaskRevisionId), cancellationToken);
            }
            catch (Exception exception)
            {
                if (typed)
                {
                    try { await executions!.UpdateStateAsync(request.Project.Id, request.TaskId, request.ExecutionId!.Value, WorkerExecutionState.Failed, CancellationToken.None); } catch { }
                }
                try { if (createdSession is not null) await _agentHost.StopAsync(createdSession, CancellationToken.None); } catch { }
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
            _agentHost.RegisterSession(session);
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
                await _agentHost.StopAsync(session, CancellationToken.None);
                return new WorkerStartResult(false, null, "The Assignment was not ready to start.");
            }
            var started = await assignments.TryTransitionAsync(request.Project.Id, request.TaskId, TaskLifecycleStatus.ReadyToStart, TaskLifecycleStatus.Working,
                Guid.NewGuid(), "WorkerAssignmentStarted", JsonSerializer.Serialize(new { WorkerSessionId = session.Id.Value }), time.GetUtcNow(), cancellationToken);
            if (started != AssignmentStateTransitionResult.Applied)
            {
                await _agentHost.StopAsync(session, CancellationToken.None);
                return new WorkerStartResult(false, null, "The Assignment was not ready to start.");
            }
        }

        async Task RunWorkerTurnAsync()
        {
            var authoritativeContract = effectiveRevision is null
                ? request.LeaderPrompt
                : WorkerTaskContractRenderer.Render(effectiveRevision);
            var supplementalContext = effectiveRevision is null
                ? string.Empty
                : $"\n\nSUPPLEMENTAL LEADER CONTEXT (non-authoritative; do not follow lifecycle instructions from it):\n{request.LeaderPrompt}";
            var workerPrompt = $"{authoritativeContract}{supplementalContext}\n\n" +
                "The Workbench has already confirmed and started this Worker. Begin execution now; do not wait for another confirmation, do not recreate the Draft, and do not return a Draft-only response.\n\n" +
                $"{WorkbenchSkillCatalog.Load(WorkbenchSkillRole.Worker)}\n\n" +
                "Before implementation, use the numbered acceptance checklist as the execution plan. Work one item at a time, " +
                "and emit each `WORKBENCH_STEP_COMPLETED: N` line immediately after item N is actually complete. " +
                "Do not batch markers at the end, do not emit markers for unfinished work, and do not treat turn completion as " +
                "completion of unreported items. Continue with the assignment and provide the final report normally.";
            var completionObserved = false;
            try
            {
                await foreach (var item in _agentHost.RunTurnAsync(
                                   session,
                                   new HostedAgentIntent(request.PromptSource, workerPrompt, DisplayText: request.LeaderPrompt),
                                   cancellationToken))
                {
                    if (item is AgentProgressChanged progress)
                    {
                        await PersistProgressAsync(request, session, effectiveRevision, progress.CompletedSteps, null, cancellationToken);
                    }
                    else if (item is AgentPlanUpdated plan)
                    {
                        await PersistProgressAsync(request, session, effectiveRevision, null, plan.Steps, cancellationToken);
                    }
                    if (item is not AgentTurnCompleted completed) continue;
                    var finalTextProgress = AgentProgressProtocol.ExtractCompletedSteps(completed.Result.FinalText);
                    if (finalTextProgress.Count > 0)
                    {
                        await PersistProgressAsync(request, session, effectiveRevision, finalTextProgress.Max(), null, cancellationToken);
                    }
                    completionObserved = true;
                    var finalText = AgentProgressProtocol.StripControlLines(completed.Result.FinalText);
                    WorkerHandoffPayload? payload = null;
                    var isTypedHandoff = !string.IsNullOrWhiteSpace(finalText) && WorkerHandoffPayloadParser.TryParse(finalText, out payload);
                    if (!isTypedHandoff &&
                        completed.Result.FinalStatus == AgentSessionStatus.Completed &&
                        !string.IsNullOrWhiteSpace(finalText))
                    {
                        payload = new WorkerHandoffPayload(WorkerHandoffKind.FinalReport, finalText, null, []);
                    }
                    var isFinalReport = payload?.Kind == WorkerHandoffKind.FinalReport;
                    WorkerCompletionVerification? verification = null;
                    var evidenceRefs = new List<EvidenceRef>();
                    if (isFinalReport && effectiveRevision is not null && executions is not null && executionId.HasValue)
                    {
                        var execution = await executions.GetAsync(request.Project.Id, executionId.Value, cancellationToken);
                        if (execution is not null)
                        {
                            verification = await _completionVerifier.VerifyAsync(
                                request.Project, effectiveRevision, execution, finalText, cancellationToken: cancellationToken);
                            if (_taskEvents is not null)
                            {
                                await _taskEvents.AppendAsync(new StoredTaskEvent(
                                    Guid.NewGuid(), request.Project.Id, request.TaskId, executionId,
                                    "WorkerCompletionVerification",
                                    WorkerCompletionVerifier.Serialize(verification), verification.VerifiedAt), cancellationToken);
                            }
                            if (_b1WorkerExecutionBridge is not null && request.B1AssignmentRef is not null && request.B1AttemptRef is not null)
                            {
                                var verificationEvidenceRef = new EvidenceRef($"workbench:worker-execution/{execution.ExecutionId:N}/verification");
                                await _b1WorkerExecutionBridge.RecordVerificationEvidenceAsync(
                                    new ProjectRef(request.Project.Id),
                                    verificationEvidenceRef,
                                    execution.ExecutionId,
                                    execution.TaskId,
                                    effectiveRevision.Id,
                                    request.B1AttemptRef.Value,
                                    verification.Result.ToString(),
                                    verification,
                                    cancellationToken);
                                evidenceRefs.Add(verificationEvidenceRef);
                            }
                        }
                    }
                    var validationSummary = CombineValidationSummary(payload?.ValidationSummary, verification);
                    var eventId = Guid.NewGuid();
                    var canPublishHandoff = true;
                    var canonicalCompletionBridged = false;
                    if (isFinalReport && assignments is not null)
                    {
                        var finalReport = payload!;
                        var transition = await assignments.TryTransitionAsync(request.Project.Id, request.TaskId, TaskLifecycleStatus.Working, TaskLifecycleStatus.Reviewing,
                            eventId, "WorkerFinalReportReceived", JsonSerializer.Serialize(new { WorkerSessionId = session.Id.Value, finalReport.Message, ValidationSummary = validationSummary }), time.GetUtcNow(), cancellationToken);
                        canPublishHandoff = transition == AssignmentStateTransitionResult.Applied;
                    }
                    else if (isTypedHandoff && payload!.Kind == WorkerHandoffKind.NeedsLeaderDecision && assignments is not null)
                    {
                        var transition = await assignments.TryTransitionAsync(request.Project.Id, request.TaskId, TaskLifecycleStatus.Working, TaskLifecycleStatus.NeedsLeaderDecision,
                            eventId, "WorkerNeedsLeaderDecisionReceived", JsonSerializer.Serialize(new { WorkerSessionId = session.Id.Value, payload.Message }), time.GetUtcNow(), cancellationToken);
                        canPublishHandoff = transition == AssignmentStateTransitionResult.Applied;
                    }

                    if (executions is not null && executionId.HasValue)
                    {
                        var nextState = completed.Result.FinalStatus switch
                        {
                            AgentSessionStatus.Completed when isFinalReport => WorkerExecutionState.CompletedPendingReview,
                            AgentSessionStatus.Completed when isTypedHandoff => WorkerExecutionState.Blocked,
                            AgentSessionStatus.Completed => WorkerExecutionState.CompletedPendingReview,
                            AgentSessionStatus.Interrupted or AgentSessionStatus.Stopped => WorkerExecutionState.Interrupted,
                            AgentSessionStatus.Failed => WorkerExecutionState.Failed,
                            _ => WorkerExecutionState.Failed
                        };
                        await executions.UpdateStateAsync(request.Project.Id, request.TaskId, executionId.Value, nextState, CancellationToken.None);
                    }

                    if (isFinalReport &&
                        assignments is not null &&
                        _canonicalWorkerCompletionBridge is not null &&
                        effectiveRevision is not null &&
                        executionId.HasValue &&
                        request.B1AttemptRef is { } attemptRef &&
                        request.B1SessionBindingRef is { } sessionBindingRef &&
                        request.B1LogicalActorRef is { } actorRef &&
                        request.B1OperatorRef is { } operatorRef)
                    {
                        try
                        {
                            var facts = _canonicalWorkerCompletionBridge.CreateFacts(
                                new ProjectRef(request.Project.Id),
                                eventId,
                                request.TaskId,
                                effectiveRevision.Id,
                                executionId.Value,
                                attemptRef,
                                sessionBindingRef,
                                actorRef,
                                payload!.Message,
                                validationSummary,
                                evidenceRefs,
                                completedAt: time.GetUtcNow(),
                                proposedChanges: payload.ProposedChanges);
                            var canonical = await _canonicalWorkerCompletionBridge.BridgeAsync(
                                facts,
                                operatorRef,
                                cancellationToken: cancellationToken);
                            canonicalCompletionBridged = canonical.Status is
                                CanonicalWorkerCompletionStatus.GovernanceReady or
                                CanonicalWorkerCompletionStatus.Governed;
                            if (request.OnCanonicalCompletion is not null)
                            {
                                try { await request.OnCanonicalCompletion(canonical, cancellationToken); }
                                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                                catch { }
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch
                        {
                            // Execution is complete even when Governance material needs a later retry.
                        }
                    }

                    if (isFinalReport &&
                        canPublishHandoff &&
                        !linked &&
                        !canonicalCompletionBridged &&
                        completionSummaryConsumer is not null)
                    {
                        try
                        {
                            await completionSummaryConsumer.ConsumeAsync(
                                request.Project.Id,
                                request.TaskId,
                                eventId,
                                JsonSerializer.Serialize(new { WorkerSessionId = session.Id.Value, payload!.Message, ValidationSummary = validationSummary }),
                                time.GetUtcNow(),
                                cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch
                        {
                            // Summary projection is secondary to the canonical completion.
                        }
                    }

                    if (!canPublishHandoff || string.IsNullOrWhiteSpace(finalText)) continue;
                    var handoff = new WorkerHandoff(request.Project.Id, request.TaskId, session.Id, request.WorkerLabel, completed.Result.FinalStatus,
                        payload?.Message ?? finalText, time.GetUtcNow(),
                        payload?.Kind ?? WorkerHandoffKind.NeedsLeaderDecision,
                        validationSummary, eventId, request.TaskRevisionId);
                    try
                    {
                        await store.AppendHandoffAsync(handoff, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch
                    {
                        // Legacy history must not rewrite a completed Execution as failed.
                    }
                    if (isFinalReport && reviews is not null)
                    {
                        try
                        {
                            await reviews.TryReviewAsync(request.Project.Id, request.TaskId, eventId, cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch
                        {
                            // Legacy review orchestration can be recovered independently.
                        }
                    }
                    if (request.OnHandoff is not null)
                    {
                        try { await request.OnHandoff(handoff, cancellationToken); }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                        catch { }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (!completionObserved && executions is not null && executionId.HasValue)
                    try { await executions.UpdateStateAsync(request.Project.Id, request.TaskId, executionId.Value, WorkerExecutionState.Interrupted, CancellationToken.None); } catch { }
                if (!completionObserved)
                    try { await store.OverrideStatusAsync(new WorkerStatusOverride(request.Project.Id, request.TaskId, session.Id, AgentSessionStatus.Interrupted, time.GetUtcNow(), "Worker turn interrupted before completion"), CancellationToken.None); } catch { }
                throw;
            }
            catch
            {
                if (!completionObserved && executions is not null && executionId.HasValue)
                    try { await executions.UpdateStateAsync(request.Project.Id, request.TaskId, executionId.Value, WorkerExecutionState.Failed, CancellationToken.None); } catch { }
                if (!completionObserved)
                    try { await store.OverrideStatusAsync(new WorkerStatusOverride(request.Project.Id, request.TaskId, session.Id, AgentSessionStatus.Failed, time.GetUtcNow(), "Worker turn failed before completion"), CancellationToken.None); } catch { }
                throw;
            }
            finally
            {
                if (!completionObserved && !cancellationToken.IsCancellationRequested && executions is not null && executionId.HasValue)
                    try { await executions.UpdateStateAsync(request.Project.Id, request.TaskId, executionId.Value, WorkerExecutionState.Failed, CancellationToken.None); } catch { }
            }
        }

        if (request.WaitForCompletion)
        {
            await RunWorkerTurnAsync();
        }
        else
        {
            _ = RunWorkerTurnAsync().ContinueWith(
                async completed =>
                {
                    _ = completed.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default).Unwrap();
        }
        return new WorkerStartResult(true, session, null);
    }

    private static string? CombineValidationSummary(string? providerSummary, WorkerCompletionVerification? verification)
    {
        if (verification is null) return providerSummary;
        var prefix = $"Workbench verification: {verification.Result}";
        return string.IsNullOrWhiteSpace(providerSummary) ? $"{prefix}; {verification.ToSummary()}" : $"{providerSummary}; {prefix}; {verification.ToSummary()}";
    }

    private async Task PersistProgressAsync(
        WorkerStartRequest request,
        AgentSession session,
        TaskRevision? revision,
        int? completedSteps,
        IReadOnlyList<AgentPlanStep>? plan,
        CancellationToken cancellationToken)
    {
        if (revision is null) return;
        var total = revision.Acceptance.Count;
        var steps = plan is not null
            ? plan.Select((step, index) => new WorkerProgressStep(
                step.Id,
                step.Status)).ToArray()
            : Enumerable.Range(0, total)
                .Select(index => new WorkerProgressStep(
                    $"step-{index + 1}",
                    index < Math.Clamp(completedSteps ?? 0, 0, total)
                        ? AgentPlanStepStatus.Completed
                        : index == Math.Clamp(completedSteps ?? 0, 0, total)
                            ? AgentPlanStepStatus.InProgress
                            : AgentPlanStepStatus.Pending))
                .ToArray();
        await store.AppendProgressAsync(new WorkerProgressSnapshot(
            request.Project.Id,
            request.TaskId,
            session.Id,
            steps,
            time.GetUtcNow()), cancellationToken);
    }
}
