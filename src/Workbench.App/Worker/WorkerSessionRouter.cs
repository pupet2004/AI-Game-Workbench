using System.Text.Json;
using Workbench.Core.Tasks;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.Runtime.Runtime;
using Workbench.Storage.Workers;

namespace Workbench.App.Worker;

public sealed record WorkerStartRequest(
    Workbench.Core.Projects.Project Project,
    Guid TaskId,
    string TaskTitle,
    ExecutionProfile ExecutionProfile,
    string LeaderPrompt,
    AgentSessionId? ReuseWorkerSessionId,
    string WorkerLabel,
    Func<WorkerHandoff, CancellationToken, Task>? OnHandoff = null);

public sealed record WorkerStartResult(bool Succeeded, AgentSession? WorkerSession, string? Error);

public sealed record WorkerSessionRecord(
    Guid ProjectId,
    Guid TaskId,
    string TaskTitle,
    AgentSession Session,
    ExecutionProfile Profile,
    string Label,
    DateTimeOffset LastActiveAt);

public sealed record WorkerHandoff(
    Guid ProjectId,
    Guid TaskId,
    AgentSessionId WorkerSessionId,
    string WorkerLabel,
    AgentSessionStatus Status,
    string Message,
    DateTimeOffset CreatedAt);

public sealed record WorkerRemoval(
    Guid ProjectId,
    Guid TaskId,
    AgentSessionId WorkerSessionId,
    DateTimeOffset RemovedAt);

public interface IWorkerRoutingStore
{
    Task SaveSessionAsync(WorkerSessionRecord session, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkerSessionRecord>> ListSessionsAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<WorkerSessionRecord?> GetSessionAsync(Guid projectId, Guid taskId, AgentSessionId sessionId, CancellationToken cancellationToken = default);
    Task AppendHandoffAsync(WorkerHandoff handoff, CancellationToken cancellationToken = default);
    Task AppendRemovalAsync(WorkerRemoval removal, CancellationToken cancellationToken = default);
}

public sealed class TaskEventWorkerRoutingStore(TaskEventRepository events) : IWorkerRoutingStore
{
    private readonly TaskEventRepository _events = events;

    public Task SaveSessionAsync(WorkerSessionRecord session, CancellationToken cancellationToken = default) =>
        _events.AppendAsync(new StoredTaskEvent(Guid.NewGuid(), session.ProjectId, session.TaskId, null,
            "WorkerSessionStarted", JsonSerializer.Serialize(StoredSession.From(session)), session.LastActiveAt), cancellationToken);

    public async Task<WorkerSessionRecord?> GetSessionAsync(Guid projectId, Guid taskId, AgentSessionId sessionId, CancellationToken cancellationToken = default)
    {
        var events = await _events.ListAsync(projectId, taskId, 200, cancellationToken);
        if (events.Where(item => item.Type == "WorkerRemoved")
            .Select(item => JsonSerializer.Deserialize<WorkerRemoval>(item.Payload))
            .Any(item => item?.WorkerSessionId == sessionId))
        {
            return null;
        }

        return events.Where(item => item.Type == "WorkerSessionStarted")
            .Select(item => JsonSerializer.Deserialize<StoredSession>(item.Payload)?.ToRecord())
            .LastOrDefault(item => item?.Session.Id == sessionId);
    }

    public async Task<IReadOnlyList<WorkerSessionRecord>> ListSessionsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var started = (await _events.ListForProjectAsync(projectId, "WorkerSessionStarted", 200, cancellationToken))
            .Select(item => JsonSerializer.Deserialize<StoredSession>(item.Payload)?.ToRecord())
            .Where(item => item is not null).Cast<WorkerSessionRecord>().ToArray();
        var removed = (await _events.ListForProjectAsync(projectId, "WorkerRemoved", 200, cancellationToken))
            .Select(item => JsonSerializer.Deserialize<WorkerRemoval>(item.Payload))
            .Where(item => item is not null).Select(item => item!.WorkerSessionId).ToHashSet();
        var sessions = new List<WorkerSessionRecord>(started.Length);
        foreach (var session in started.Where(item => !removed.Contains(item.Session.Id)))
        {
            var handoff = (await _events.ListAsync(projectId, session.TaskId, 200, cancellationToken))
                .Where(item => item.Type == "WorkerToLeaderHandoff")
                .Select(item => JsonSerializer.Deserialize<WorkerHandoff>(item.Payload))
                .Where(item => item?.WorkerSessionId == session.Session.Id)
                .OrderByDescending(item => item!.CreatedAt)
                .FirstOrDefault();
            sessions.Add(handoff is null
                ? session
                : session with { Session = session.Session with { Status = handoff.Status, UpdatedAt = handoff.CreatedAt }, LastActiveAt = handoff.CreatedAt });
        }

        return sessions;
    }

    public Task AppendHandoffAsync(WorkerHandoff handoff, CancellationToken cancellationToken = default) =>
        _events.AppendAsync(new StoredTaskEvent(Guid.NewGuid(), handoff.ProjectId, handoff.TaskId, null,
            "WorkerToLeaderHandoff", JsonSerializer.Serialize(handoff), handoff.CreatedAt), cancellationToken);

    public Task AppendRemovalAsync(WorkerRemoval removal, CancellationToken cancellationToken = default) =>
        _events.AppendAsync(new StoredTaskEvent(Guid.NewGuid(), removal.ProjectId, removal.TaskId, null,
            "WorkerRemoved", JsonSerializer.Serialize(removal), removal.RemovedAt), cancellationToken);
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

public sealed class WorkerSessionRouter(AgentRuntimeRegistry runtimes, IWorkerRoutingStore store, TimeProvider time)
{
    public async Task<WorkerStartResult> StartAsync(WorkerStartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.LeaderPrompt);
        var runtime = runtimes.GetByAccount(new Workbench.Runtime.Providers.ProviderAccountId(Guid.Parse(request.ExecutionProfile.ProviderAccountId)));
        AgentSession session;
        var created = request.ReuseWorkerSessionId is null;
        if (created)
        {
            session = await runtime.CreateSessionAsync(new CreateAgentSessionRequest(
                runtime.Account.Id, request.ExecutionProfile.ModelProfileId, request.Project.RootPath), cancellationToken);
            try
            {
                await store.SaveSessionAsync(new WorkerSessionRecord(request.Project.Id, request.TaskId, request.TaskTitle, session,
                    request.ExecutionProfile, request.WorkerLabel, time.GetUtcNow()), cancellationToken);
            }
            catch (Exception exception)
            {
                try { await runtime.StopAsync(session, CancellationToken.None); } catch { }
                return new WorkerStartResult(false, null, exception.Message);
            }
        }
        else
        {
            var reuseSessionId = request.ReuseWorkerSessionId.GetValueOrDefault();
            var persisted = await store.GetSessionAsync(request.Project.Id, request.TaskId, reuseSessionId, cancellationToken);
            if (persisted is null || persisted.Session.AccountId != runtime.Account.Id)
            {
                return new WorkerStartResult(false, null, "The requested Worker session is not available for this task and account.");
            }
            session = persisted.Session;
        }

        await foreach (var item in runtime.SendAsync(session, new AgentRequest(request.LeaderPrompt), cancellationToken))
        {
            if (item is AgentTurnCompleted completed && !string.IsNullOrWhiteSpace(completed.Result.FinalText))
            {
                var handoff = new WorkerHandoff(request.Project.Id, request.TaskId, session.Id,
                    request.WorkerLabel, completed.Result.FinalStatus, completed.Result.FinalText, time.GetUtcNow());
                await store.AppendHandoffAsync(handoff, cancellationToken);
                if (request.OnHandoff is not null)
                {
                    try { await request.OnHandoff(handoff, cancellationToken); }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch { }
                }
            }
        }
        return new WorkerStartResult(true, session, null);
    }
}
