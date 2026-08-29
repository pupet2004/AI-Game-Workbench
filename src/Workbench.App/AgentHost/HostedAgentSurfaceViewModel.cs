using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Workbench.Runtime.Agents;
using Workbench.App.Services;

namespace Workbench.App.AgentHost;

public enum HostedSurfaceEntryKind
{
    UserMessage,
    LeaderMessage,
    AssistantDelta,
    ToolActivity,
    Approval,
    Question,
    Error,
    TurnCompleted
}

public sealed record HostedSurfaceEntry(
    HostedSurfaceEntryKind Kind,
    AgentIntentSource Source,
    string Text,
    DateTimeOffset OccurredAt,
    bool IsComplete = true)
{
    public string Label => Kind switch
    {
        HostedSurfaceEntryKind.UserMessage => "你",
        HostedSurfaceEntryKind.LeaderMessage => "Leader",
        HostedSurfaceEntryKind.AssistantDelta => "Worker",
        HostedSurfaceEntryKind.ToolActivity => "活动",
        HostedSurfaceEntryKind.Approval => "审批",
        HostedSurfaceEntryKind.Question => "需要输入",
        HostedSurfaceEntryKind.Error => "错误",
        HostedSurfaceEntryKind.TurnCompleted => "完成",
        _ => string.Empty
    };
}

/// <summary>
/// Thin interaction model for any hosted-session surface. It deliberately
/// contains no provider/runtime ownership and can be hosted by Avalonia,
/// WebView2, a detached window, or a future taskbar surface.
/// </summary>
public sealed partial class HostedAgentSurfaceViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IAgentHost _host;
    private readonly object _sync = new();
    private readonly SynchronizationContext? _surfaceContext;
    private CancellationTokenSource? _turnCancellation;
    private AgentApprovalRequested? _pendingApproval;
    private AgentQuestionRequested? _pendingQuestion;

    public HostedAgentSurfaceViewModel(IAgentHost host, AgentSession session)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Session = session ?? throw new ArgumentNullException(nameof(session));
        _surfaceContext = SynchronizationContext.Current;
        _host.IntentReceived += OnIntentReceived;
        _host.EventReceived += OnEventReceived;
        _host.RegisterSession(session);
        RefreshFromSnapshot(_host.Attach(session));
    }

    public AgentSession Session { get; private set; }

    public ObservableCollection<HostedSurfaceEntry> Entries { get; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string DraftText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "Ready";

    public AgentApprovalRequested? PendingApproval => _pendingApproval;

    public AgentQuestionRequested? PendingQuestion => _pendingQuestion;

    public async Task SendAsync(
        string text,
        AgentIntentSource source = AgentIntentSource.User,
        IReadOnlyList<AgentInputPart>? inputs = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Error = null;
        if (IsBusy)
        {
            await SteerAsync(text, source, cancellationToken).ConfigureAwait(false);
            return;
        }

        _turnCancellation?.Dispose();
        _turnCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsBusy = true;
        StatusText = LocalizationService.Current["Dynamic.Working"];
        try
        {
            await foreach (var agentEvent in _host.RunTurnAsync(
                               Session,
                               new HostedAgentIntent(source, text, inputs),
                               _turnCancellation.Token))
            {
                // The Host broadcasts each event to every attached Surface.
                // Consuming it here as well would duplicate transcript rows.
            }
        }
        catch (OperationCanceledException) when (_turnCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Error = exception.Message;
            Entries.Add(new HostedSurfaceEntry(HostedSurfaceEntryKind.Error, AgentIntentSource.Workbench, exception.Message, DateTimeOffset.UtcNow));
        }
        finally
        {
            IsBusy = false;
            if (string.Equals(StatusText, LocalizationService.Current["Dynamic.Working"], StringComparison.Ordinal))
                StatusText = LocalizationService.Current["Dynamic.Ready"];
            _turnCancellation?.Dispose();
            _turnCancellation = null;
        }
    }

    public Task SteerAsync(
        string text,
        AgentIntentSource source = AgentIntentSource.User,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return _host.SteerAsync(Session, new HostedAgentIntent(source, text), cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _host.StopAsync(Session, cancellationToken);

    public async Task RespondToApprovalAsync(
        AgentApprovalDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        await _host.RespondToApprovalAsync(Session, decision, cancellationToken);
        _pendingApproval = null;
        OnPropertyChanged(nameof(PendingApproval));
    }

    public async Task RespondToQuestionAsync(
        string requestId,
        IReadOnlyDictionary<string, string> answers,
        CancellationToken cancellationToken = default)
    {
        await _host.RespondToQuestionAsync(Session, requestId, answers, cancellationToken);
        _pendingQuestion = null;
        OnPropertyChanged(nameof(PendingQuestion));
    }

    public void Refresh()
    {
        RefreshFromSnapshot(_host.Attach(Session));
    }

    public async Task HydrateTranscriptAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var transcript = await _host.GetTranscriptAsync(Session, cancellationToken);
            if (transcript.Count == 0 || Entries.Any(item =>
                    item.Kind is HostedSurfaceEntryKind.UserMessage or HostedSurfaceEntryKind.AssistantDelta or HostedSurfaceEntryKind.TurnCompleted))
                return;

            foreach (var item in transcript.OfType<AgentMessage>())
            {
                var text = item.Role == AgentMessageRole.User
                    ? DisplayIntentText(item.Text)
                    : AgentProgressProtocol.StripControlLines(item.Text);
                if (string.IsNullOrWhiteSpace(text)) continue;
                Entries.Add(new HostedSurfaceEntry(
                    item.Role == AgentMessageRole.User ? HostedSurfaceEntryKind.UserMessage : HostedSurfaceEntryKind.TurnCompleted,
                    item.Role == AgentMessageRole.User ? AgentIntentSource.User : AgentIntentSource.Workbench,
                    text,
                    item.OccurredAt));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // Provider transcript hydration is best effort.
        }
    }

    public ValueTask DisposeAsync()
    {
        _host.IntentReceived -= OnIntentReceived;
        _host.EventReceived -= OnEventReceived;
        // Closing a Surface must not stop the hosted Agent turn. Stop is an
        // explicit user intent and is exposed separately.
        if (!IsBusy)
        {
            _turnCancellation?.Dispose();
            _turnCancellation = null;
        }
        return ValueTask.CompletedTask;
    }

    private void OnIntentReceived(object? sender, HostedAgentIntentReceived item)
    {
        try
        {
            if (item.SessionId != Session.Id) return;
            if (!TryGetIntentKind(item.Intent.Source, out var kind)) return;
            DispatchToSurface(() => Entries.Add(new HostedSurfaceEntry(
                kind, item.Intent.Source, DisplayIntentText(item.Intent.DisplayText ?? item.Intent.Text), item.Intent.EffectiveSubmittedAt)));
        }
        catch
        {
            // A detached Surface must never fail the hosted runtime turn.
        }
    }

    private void OnEventReceived(object? sender, HostedAgentEvent item)
    {
        try
        {
            if (item.SessionId != Session.Id) return;
            DispatchToSurface(() => AppendEvent(item.Event, AgentIntentSource.Workbench));
        }
        catch
        {
            // A detached Surface must never fail the hosted runtime turn.
        }
    }

    private void DispatchToSurface(Action action)
    {
        if (_surfaceContext is null || ReferenceEquals(SynchronizationContext.Current, _surfaceContext))
        {
            action();
            return;
        }

        _surfaceContext.Post(static state => ((Action)state!).Invoke(), action);
    }

    private void RefreshFromSnapshot(HostedAgentSessionSnapshot snapshot)
    {
        Session = snapshot.Session;
        Entries.Clear();
        foreach (var intent in snapshot.Intents)
        {
            if (TryGetIntentKind(intent.Source, out var kind))
                Entries.Add(new HostedSurfaceEntry(kind, intent.Source, DisplayIntentText(intent.DisplayText ?? intent.Text), intent.EffectiveSubmittedAt));
        }

        foreach (var agentEvent in snapshot.Events)
        {
            AppendEvent(agentEvent, AgentIntentSource.Workbench);
        }

        IsBusy = snapshot.HasActiveTurn;
        StatusText = snapshot.HasActiveTurn ? LocalizationService.Current["Dynamic.Working"] : Session.Status switch
        {
            AgentSessionStatus.WaitingApproval => LocalizationService.Current["Dynamic.Waiting"],
            AgentSessionStatus.Completed => LocalizationService.Current["Dynamic.Completed"],
            AgentSessionStatus.Failed => LocalizationService.Current["Dynamic.Failed"],
            AgentSessionStatus.Interrupted or AgentSessionStatus.Stopped => LocalizationService.Current["Dynamic.Interrupted"],
            _ => LocalizationService.Current["Dynamic.Ready"]
        };
    }

    private static bool TryGetIntentKind(AgentIntentSource source, out HostedSurfaceEntryKind kind)
    {
        kind = source switch
        {
            AgentIntentSource.User => HostedSurfaceEntryKind.UserMessage,
            AgentIntentSource.Leader => HostedSurfaceEntryKind.LeaderMessage,
            _ => default
        };
        return source is AgentIntentSource.User or AgentIntentSource.Leader;
    }

    private static string DisplayIntentText(string text)
    {
        var marker = text.IndexOf("\n---\nname: workbench-worker", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            marker = text.IndexOf("\n# Workbench Worker", StringComparison.Ordinal);
        return marker >= 0 ? text[..marker].TrimEnd() : text;
    }

    private void AppendEvent(AgentEvent agentEvent, AgentIntentSource source)
    {
        switch (agentEvent)
        {
            case AgentTextDelta delta:
                var visibleText = AgentProgressProtocol.StripControlLines(delta.Text);
                if (string.IsNullOrEmpty(visibleText)) break;
                var previous = Entries.LastOrDefault();
                if (previous is { Kind: HostedSurfaceEntryKind.AssistantDelta, IsComplete: false } && previous.Source == source)
                {
                    Entries[^1] = previous with { Text = previous.Text + visibleText, OccurredAt = delta.OccurredAt };
                }
                else
                {
                    Entries.Add(new HostedSurfaceEntry(HostedSurfaceEntryKind.AssistantDelta, source, visibleText, delta.OccurredAt, false));
                }
                break;
            case AgentStatusChanged status:
                IsBusy = status.Status is AgentSessionStatus.Running or AgentSessionStatus.WaitingApproval;
                StatusText = status.Status switch
                {
                    AgentSessionStatus.Running => LocalizationService.Current["Dynamic.Working"],
                    AgentSessionStatus.WaitingApproval => LocalizationService.Current["Dynamic.Waiting"],
                    AgentSessionStatus.Completed => LocalizationService.Current["Dynamic.Completed"],
                    AgentSessionStatus.Failed => LocalizationService.Current["Dynamic.Failed"],
                    AgentSessionStatus.Interrupted or AgentSessionStatus.Stopped => LocalizationService.Current["Dynamic.Interrupted"],
                    _ => LocalizationService.Current["Dynamic.Ready"]
                };
                break;
            case AgentToolEvent tool:
                // Reasoning is provider-internal metadata. It is intentionally
                // omitted instead of leaking a meaningless "Thinking" row.
                if (string.Equals(tool.ToolName, "reasoning", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tool.ToolName, "thinking", StringComparison.OrdinalIgnoreCase))
                    break;
                var toolText = string.IsNullOrWhiteSpace(tool.Detail) ? tool.ToolName : $"{tool.ToolName}: {tool.Detail}";
                var previousTool = Entries.LastOrDefault(item => item.Kind == HostedSurfaceEntryKind.ToolActivity);
                if (previousTool is { IsComplete: false } && string.Equals(previousTool.Text, toolText, StringComparison.Ordinal))
                {
                    var index = Entries.IndexOf(previousTool);
                    Entries[index] = previousTool with { IsComplete = tool.IsCompleted, OccurredAt = tool.OccurredAt };
                }
                else
                {
                    Entries.Add(new HostedSurfaceEntry(HostedSurfaceEntryKind.ToolActivity, AgentIntentSource.Workbench,
                        toolText, tool.OccurredAt, tool.IsCompleted));
                }
                break;
            case AgentApprovalRequested approval:
                // Worker approvals are governed from the Leader surface. Keep
                // the Worker transcript informative without exposing a second
                // competing approval control.
                _pendingApproval = null;
                OnPropertyChanged(nameof(PendingApproval));
                Entries.Add(new HostedSurfaceEntry(HostedSurfaceEntryKind.Approval, AgentIntentSource.Workbench,
                    $"已转交 Leader 审核：{approval.Summary}", approval.OccurredAt));
                break;
            case AgentQuestionRequested question:
                _pendingQuestion = question;
                OnPropertyChanged(nameof(PendingQuestion));
                Entries.Add(new HostedSurfaceEntry(HostedSurfaceEntryKind.Question, AgentIntentSource.Workbench, question.Prompt, question.OccurredAt));
                break;
            case AgentError error:
                Error = error.Message;
                Entries.Add(new HostedSurfaceEntry(HostedSurfaceEntryKind.Error, AgentIntentSource.Workbench, error.Message, error.OccurredAt));
                break;
            case AgentTurnCompleted completed:
                IsBusy = false;
                Session = Session with
                {
                    Status = completed.Result.FinalStatus,
                    UpdatedAt = completed.OccurredAt
                };
                OnPropertyChanged(nameof(Session));
                StatusText = completed.Result.FinalStatus switch
                {
                    AgentSessionStatus.Completed => LocalizationService.Current["Dynamic.Completed"],
                    AgentSessionStatus.Failed => LocalizationService.Current["Dynamic.Failed"],
                    AgentSessionStatus.Interrupted or AgentSessionStatus.Stopped => LocalizationService.Current["Dynamic.Interrupted"],
                    AgentSessionStatus.WaitingApproval => LocalizationService.Current["Dynamic.Waiting"],
                    _ => LocalizationService.Current["Dynamic.Ready"]
                };
                var last = Entries.LastOrDefault();
                if (last is { Kind: HostedSurfaceEntryKind.AssistantDelta, IsComplete: false })
                    Entries[^1] = last with { IsComplete = true };
                Entries.Add(new HostedSurfaceEntry(HostedSurfaceEntryKind.TurnCompleted, AgentIntentSource.Workbench,
                    AgentProgressProtocol.StripControlLines(completed.Result.FinalText) is { Length: > 0 } finalText
                        ? finalText
                        : completed.Result.FinalStatus.ToString(), completed.OccurredAt));
                break;
        }
    }
}
