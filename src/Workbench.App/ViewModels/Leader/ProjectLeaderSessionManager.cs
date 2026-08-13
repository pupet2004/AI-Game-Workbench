using System.Collections.ObjectModel;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Registry;
using Workbench.Storage.Leaders;
using Workbench.App.Worker;

namespace Workbench.App.ViewModels.Leader;

public sealed class ProjectLeaderSessionManager
{
    private readonly Dictionary<Guid, LeaderConversationState> _conversations = [];
    private readonly ProjectLeaderRepository? _leaders;
    private readonly LeaderSessionEpochRepository? _epochs;
    private readonly LeaderMessageRepository? _messages;
    private readonly TimeProvider _timeProvider;

    public ProjectLeaderSessionManager(
        ProjectLeaderRepository? leaders = null,
        LeaderSessionEpochRepository? epochs = null,
        LeaderMessageRepository? messages = null,
        TimeProvider? timeProvider = null)
    {
        if ((leaders is null) != (epochs is null) || (leaders is null) != (messages is null))
        {
            throw new ArgumentException("Leader persistence repositories must be supplied together.");
        }

        _leaders = leaders;
        _epochs = epochs;
        _messages = messages;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal LeaderConversationState GetOrCreate(Guid projectId)
    {
        if (_conversations.TryGetValue(projectId, out var conversation))
        {
            return conversation;
        }

        conversation = new LeaderConversationState(projectId);
        _conversations.Add(projectId, conversation);
        return conversation;
    }

    internal async Task LoadAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var conversation = GetOrCreate(projectId);
        await conversation.LoadGate.WaitAsync(cancellationToken);
        try
        {
            if (conversation.IsLoaded)
            {
                return;
            }

            if (_leaders is null)
            {
                conversation.IsLoaded = true;
                return;
            }

            var leader = await _leaders.GetAsync(projectId, cancellationToken);
            if (leader?.CurrentEpochId is not Guid epochId)
            {
                conversation.IsLoaded = true;
                return;
            }

            var epoch = await _epochs!.GetAsync(epochId, cancellationToken);
            if (epoch is null || epoch.ProjectId != projectId || epoch.EndedAt is not null)
            {
                conversation.IsLoaded = true;
                return;
            }

            conversation.Epoch = epoch;
            conversation.Session = RestoreSession(epoch);
            conversation.SessionNeedsResume = true;
            var persistedModel = CreatePersistedModelOption(epoch);
            conversation.SelectedModel = persistedModel;
            conversation.AvailableModels.Add(persistedModel);
            foreach (var message in await _messages!.GetAllAsync(epoch.Id, cancellationToken))
            {
                conversation.Messages.Add(new LeaderMessageViewModel(
                    message.Role == "user" ? LeaderMessageRole.User : LeaderMessageRole.Assistant,
                    message.Text));
            }

            conversation.IsLoaded = true;
        }
        finally
        {
            conversation.LoadGate.Release();
        }
    }

    internal async Task PersistNewEpochAsync(
        LeaderConversationState conversation,
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        if (_leaders is null)
        {
            conversation.Epoch ??= CreateEpoch(conversation.ProjectId, session);
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var epoch = CreateEpoch(conversation.ProjectId, session, now);
        await _leaders.CreateCurrentEpochAsync(
            new StoredProjectLeader(conversation.ProjectId, null, now, now),
            epoch,
            cancellationToken);
        conversation.Epoch = epoch;
    }

    internal async Task PersistUserMessageAsync(
        LeaderConversationState conversation,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (_messages is null || conversation.Epoch is null)
        {
            return;
        }

        await _messages.AppendAsync(
            conversation.Epoch.Id,
            "user",
            text,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    internal async Task PersistCompletedAssistantAsync(
        LeaderConversationState conversation,
        string finalText,
        CancellationToken cancellationToken = default)
    {
        if (conversation.Epoch is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        if (_messages is not null && !string.IsNullOrWhiteSpace(finalText))
        {
            await _messages.AppendAsync(
                conversation.Epoch.Id,
                "assistant",
                finalText,
                now,
                cancellationToken);
        }

        conversation.Epoch = conversation.Epoch with { LastActiveAt = now };
        if (_epochs is not null)
        {
            await _epochs.SaveAsync(conversation.Epoch, cancellationToken);
        }
    }

    internal async Task IngestWorkerHandoffAsync(
        WorkerHandoff handoff,
        CancellationToken cancellationToken = default)
    {
        var conversation = GetOrCreate(handoff.ProjectId);
        if (conversation.Epoch is null)
        {
            return;
        }

        var text = $"Worker · {handoff.WorkerLabel}\nTask: {handoff.TaskId}\nWorker Session: {handoff.WorkerSessionId.Value}\nStatus: {handoff.Status}\n\n{handoff.Message}";
        await PersistCompletedAssistantAsync(conversation, text, cancellationToken);
        conversation.Messages.Add(new LeaderMessageViewModel(LeaderMessageRole.Assistant, text));
    }

    internal async Task MarkBootContextDeliveredAsync(
        LeaderConversationState conversation,
        CancellationToken cancellationToken = default)
    {
        if (conversation.Epoch is null || conversation.Epoch.BootContextDeliveredAt is not null)
        {
            return;
        }

        var deliveredAt = _timeProvider.GetUtcNow();
        if (_epochs is not null)
        {
            await _epochs.MarkBootContextDeliveredAsync(conversation.Epoch.Id, deliveredAt, cancellationToken);
        }

        conversation.Epoch = conversation.Epoch with { BootContextDeliveredAt = deliveredAt };
    }

    public int Count => _conversations.Count;

    private StoredLeaderSessionEpoch CreateEpoch(Guid projectId, AgentSession session) =>
        CreateEpoch(projectId, session, _timeProvider.GetUtcNow());

    private static StoredLeaderSessionEpoch CreateEpoch(
        Guid projectId,
        AgentSession session,
        DateTimeOffset now) =>
        new(
            Guid.NewGuid(),
            projectId,
            session.ProviderId.Value,
            session.AccountId.Value,
            session.ModelId,
            session.Id.Value,
            session.ExternalSessionId,
            session.WorkingDirectory,
            now,
            now,
            null,
            null,
            null);

    private static AgentSession RestoreSession(StoredLeaderSessionEpoch epoch) =>
        new(
            new AgentSessionId(epoch.AgentSessionId),
            new ProviderAccountId(epoch.ProviderAccountId),
            new ProviderId(epoch.ProviderId),
            epoch.ModelId,
            epoch.WorkingDirectory,
            epoch.ExternalSessionId,
            AgentSessionStatus.Ready,
            epoch.StartedAt,
            epoch.LastActiveAt);

    private static LeaderModelOptionViewModel CreatePersistedModelOption(StoredLeaderSessionEpoch epoch) =>
        new(
            new AvailableModelProfile(
                new ProviderAccountId(epoch.ProviderAccountId),
                new ModelProfile(
                    new ProviderId(epoch.ProviderId),
                    epoch.ModelId,
                    epoch.ModelId,
                    AgentCapability.None)),
            epoch.ProviderId,
            $"Account {epoch.ProviderAccountId:N}");
}

internal sealed class LeaderConversationState(Guid projectId)
{
    public Guid ProjectId { get; } = projectId;

    public ObservableCollection<LeaderModelOptionViewModel> AvailableModels { get; } = [];

    public ObservableCollection<LeaderMessageViewModel> Messages { get; } = [];

    public SemaphoreSlim LoadGate { get; } = new(1, 1);

    public LeaderModelOptionViewModel? SelectedModel { get; set; }

    public AgentSession? Session { get; set; }

    public StoredLeaderSessionEpoch? Epoch { get; set; }

    public AgentApprovalRequested? PendingApproval { get; set; }

    public bool IsLoaded { get; set; }

    public bool SessionNeedsResume { get; set; }

    public bool ModelsLoaded { get; set; }

    public bool RuntimeAccountAvailable { get; set; }

    public bool IsBusy { get; set; }

    public bool IsApprovalResponding { get; set; }

    public bool IsRolloverRunning { get; set; }

    public bool HasPendingRotationDecision { get; set; }

    public string? RuntimeStatus { get; set; }

    public string? RuntimeErrorDetail { get; set; }

    public string? ApprovalError { get; set; }

    public string? RotationMessage { get; set; }
}
