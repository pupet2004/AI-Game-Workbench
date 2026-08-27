using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Providers.Codex;

namespace AgentNativeSurfaceSpike;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.Ordinal))
        {
            FakeRelaySelfTest.RunAsync().GetAwaiter().GetResult();
            return;
        }
        if (args.Contains("--real-codex-smoke", StringComparer.Ordinal))
        {
            RealCodexRelaySmokeTest.RunAsync().GetAwaiter().GetResult();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new SpikeForm(args.Contains("--real-codex", StringComparer.Ordinal)));
    }
}

internal sealed class SpikeForm : Form
{
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 24, Text = "Starting Fake Relay…", Padding = new Padding(8, 4, 8, 0) };
    private readonly ISurfaceRelay _relay;

    public SpikeForm(bool useRealCodex)
    {
        _relay = useRealCodex ? new RealCodexRelay() : new FakeRelayAdapter();
        Text = "Workbench Agent Native Surface Spike (Fake Relay)";
        Width = 1280;
        Height = 860;
        Controls.Add(_webView);
        Controls.Add(_status);
        Shown += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await _webView.EnsureCoreWebView2Async();
        _webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _webView.CoreWebView2.NavigationCompleted += (_, args) =>
        {
            _status.Text = args.IsSuccess ? "Fake Relay connected · Host owns state" : $"Navigation failed: {args.WebErrorStatus}";
        };

        var htmlPath = Path.Combine(AppContext.BaseDirectory, "Web", "index.html");
        _webView.Source = new Uri(Path.GetFullPath(htmlPath));
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            await _relay.HandleAsync(args.TryGetWebMessageAsString(), SendToSurface);
        }
        catch (Exception error)
        {
            _status.Text = $"Relay error: {error.Message}";
        }
    }

    private void SendToSurface(object message)
    {
        if (IsDisposed) return;
        void Post()
        {
            if (_webView.CoreWebView2 is null) return;
            var json = JsonSerializer.Serialize(message);
            _webView.CoreWebView2.PostWebMessageAsJson(json);
        }

        if (InvokeRequired) BeginInvoke(Post);
        else Post();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _relay.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.OnFormClosed(e);
    }
}

internal interface ISurfaceRelay : IAsyncDisposable
{
    Task HandleAsync(string json, Action<object> send);
}

internal sealed class FakeRelayAdapter : ISurfaceRelay
{
    private readonly FakeRelay _relay = new();

    public Task HandleAsync(string json, Action<object> send)
    {
        _relay.Handle(json, send);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class RealCodexRelay : ISurfaceRelay
{
    private static readonly ProviderAccountId AccountId =
        new(Guid.Parse("c0de0001-4a49-4741-8d45-574f524b424e"));
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly object _gate = new();
    private CodexAgentRuntime? _runtime;
    private AgentSession? _session;
    private CancellationTokenSource? _turnCancellation;
    private Action<object>? _send;
    private long _sequence;
    private int _turnNumber;
    private bool _hasStartedTurn;

    public async Task HandleAsync(string json, Action<object> send)
    {
        _send = send;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("type").GetString() == "ready")
        {
            await EnsureConnectedAsync(CancellationToken.None).ConfigureAwait(false);
            SendInitialize();
            await SendTranscriptAsync(CancellationToken.None).ConfigureAwait(false);
            return;
        }

        if (root.GetProperty("type").GetString() != "command") return;
        await EnsureConnectedAsync(CancellationToken.None).ConfigureAwait(false);
        switch (root.GetProperty("command").GetString())
        {
            case "send_turn": StartTurn(root.GetProperty("text").GetString() ?? string.Empty); break;
            case "respond_approval": await RespondApprovalAsync(root).ConfigureAwait(false); break;
            case "interrupt": await InterruptAsync().ConfigureAwait(false); break;
            case "steer": Emit("steer_unavailable", new { reason = "Codex runtime has no steer capability in the current adapter." }); break;
            case "attach": Attach(root); break;
            case "reload_transcript": await SendTranscriptAsync(CancellationToken.None).ConfigureAwait(false); break;
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_runtime is not null && _session is not null) return;
        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_runtime is not null && _session is not null) return;
            var options = CreateOptions();
            _runtime = await CodexAgentRuntime.ConnectAsync(options, AccountId, cancellationToken).ConfigureAwait(false);
            var requestedModel = Environment.GetEnvironmentVariable("WORKBENCH_CODEX_MODEL");
            var models = await _runtime.GetModelsAsync(cancellationToken).ConfigureAwait(false);
            var model = models.FirstOrDefault(item => string.Equals(item.ModelId, requestedModel, StringComparison.OrdinalIgnoreCase))
                ?? models.FirstOrDefault()
                ?? throw new InvalidOperationException("Codex returned no available models.");
            _session = await _runtime.CreateSessionAsync(
                new CreateAgentSessionRequest(AccountId, model.ModelId, options.WorkingDirectory),
                cancellationToken).ConfigureAwait(false);
        }
        finally { _connectGate.Release(); }
    }

    private static CodexAppServerOptions CreateOptions()
    {
        var workingDirectory = Environment.GetEnvironmentVariable("WORKBENCH_CODEX_CWD")
            ?? Environment.CurrentDirectory;
        var explicitExecutable = Environment.GetEnvironmentVariable("WORKBENCH_CODEX_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(explicitExecutable))
        {
            var entry = Environment.GetEnvironmentVariable("WORKBENCH_CODEX_ENTRY")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai", "codex", "bin", "codex.js");
            return new CodexAppServerOptions(explicitExecutable, [entry, "app-server", "--stdio"], workingDirectory, TimeSpan.FromMinutes(2));
        }

        var cli = Environment.GetEnvironmentVariable("CODEX_CLI_PATH")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenAI", "Codex", "bin", "codex.exe");
        return new CodexAppServerOptions(cli, ["app-server", "--stdio"], workingDirectory, TimeSpan.FromMinutes(2));
    }

    private void SendInitialize()
    {
        var session = _session ?? throw new InvalidOperationException("Codex session is not initialized.");
        EmitRaw(new
        {
            type = "initialize",
            session = new
            {
                projectId = "project-real-codex-spike",
                role = "leader",
                assignmentId = "assignment-real-codex-relay",
                attemptId = "attempt-real-codex-0001",
                agentSessionId = session.Id.ToString(),
                externalSessionId = session.ExternalSessionId,
                provider = "codex",
                model = session.ModelId,
                capabilities = new { streaming = true, approval = true, interrupt = true, steer = false, attachments = true, transcript = true }
            }
        });
    }

    private void StartTurn(string text)
    {
        var session = _session ?? throw new InvalidOperationException("Codex session is not initialized.");
        var turnNumber = Interlocked.Increment(ref _turnNumber);
        var turnId = $"turn-{turnNumber:0000}";
        var messageId = $"assistant-{turnNumber:0000}";
        _hasStartedTurn = true;
        Emit("user_message", new { messageId = $"user-{turnNumber:0000}", text });
        Emit("turn_started", new { turnId });
        _turnCancellation?.Cancel();
        _turnCancellation = new CancellationTokenSource();
        _ = RunTurnAsync(session, turnId, messageId, text, _turnCancellation.Token);
    }

    private async Task RunTurnAsync(AgentSession session, string turnId, string messageId, string text, CancellationToken cancellationToken)
    {
        try
        {
            var finalText = new System.Text.StringBuilder();
            var prompt = BuildWorkbenchPrompt(text);
            await foreach (var agentEvent in _runtime!.SendAsync(session, new AgentRequest(prompt), cancellationToken).ConfigureAwait(false))
            {
                switch (agentEvent)
                {
                    case AgentTextDelta delta:
                        finalText.Append(delta.Text);
                        Emit("assistant_delta", new { messageId, text = delta.Text });
                        break;
                    case AgentApprovalRequested approval:
                        Emit("approval_requested", new
                        {
                            requestId = approval.RequestId.ToString(),
                            description = approval.Summary,
                            options = approval.Options.Select(option => new { id = option.Id, label = option.Label, description = option.Description }).ToArray()
                        });
                        break;
                    case AgentToolEvent tool:
                        Emit("tool_event", new { name = tool.ToolName, detail = tool.Detail });
                        break;
                    case AgentError error:
                        Emit("error", new { message = error.Message });
                        break;
                    case AgentTurnCompleted completed:
                        if (finalText.Length == 0 && !string.IsNullOrWhiteSpace(completed.Result.FinalText))
                        {
                            finalText.Append(completed.Result.FinalText);
                            Emit("assistant_delta", new { messageId, text = completed.Result.FinalText });
                        }
                        Emit("turn_completed", new { turnId, status = completed.Result.FinalStatus.ToString(), error = completed.Result.Error });
                        Emit("handoff_created", new { handoffId = $"handoff-{_turnNumber:0000}", authorityDecisionCreated = false, text = finalText.ToString() });
                        return;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Emit("error", new { message = error.Message }); }
    }

    private async Task RespondApprovalAsync(JsonElement root)
    {
        var requestId = new AgentApprovalRequestId(Guid.Parse(root.GetProperty("requestId").GetString()!));
        var option = root.GetProperty("option").GetString() ?? "decline";
        await _runtime!.RespondToApprovalAsync(_session!, new AgentApprovalDecision(requestId, option), CancellationToken.None).ConfigureAwait(false);
        Emit("approval_resolved", new { requestId = requestId.ToString(), option });
    }

    private async Task InterruptAsync()
    {
        if (_runtime is not null && _session is not null)
        {
            await _runtime.StopAsync(_session).ConfigureAwait(false);
            Emit("turn_interrupted", new { reason = "user_stop" });
        }
    }

    private void Attach(JsonElement root)
    {
        var file = root.GetProperty("file");
        Emit("attachment_received", new
        {
            token = $"attachment-token-{Guid.NewGuid():N}",
            name = file.GetProperty("name").GetString(),
            size = file.GetProperty("size").GetInt64(),
            mediaType = file.GetProperty("type").GetString()
        });
    }

    private async Task SendTranscriptAsync(CancellationToken cancellationToken)
    {
        if (!_hasStartedTurn)
        {
            Emit("transcript", new { messages = Array.Empty<object>() });
            return;
        }

        var transcript = await _runtime!.GetTranscriptAsync(_session!, cancellationToken).ConfigureAwait(false);
        var messages = transcript.OfType<AgentMessage>().Select((message, index) => new
        {
            id = $"transcript-{index:0000}",
            role = message.Role == AgentMessageRole.User ? "user" : "assistant",
            text = message.Text
        }).ToArray();
        Emit("transcript", new { messages });
    }

    private void Emit(string eventType, object payload) => EmitRaw(new { type = "event", sequence = NextSequence(), eventType, payload });
    private void EmitRaw(object message) => _send?.Invoke(message);
    private long NextSequence() { lock (_gate) return ++_sequence; }

    public async ValueTask DisposeAsync()
    {
        _turnCancellation?.Cancel();
        if (_runtime is not null) await _runtime.DisposeAsync().ConfigureAwait(false);
        _connectGate.Dispose();
    }

    private static string BuildWorkbenchPrompt(string userText)
    {
        var skillPath = FindRepositoryFile(Path.Combine("skills", "workbench-leader", "SKILL.md"));
        var skill = skillPath is null ? "" : File.ReadAllText(skillPath).Trim();
        return $"""
            {skill}

            WORKBENCH RELAY SPIKE BINDING
            Role: Leader
            Assignment: assignment-real-codex-relay
            Attempt: attempt-real-codex-0001
            This turn is a non-authoritative experiment. Do not treat its result as Accepted Project State.

            USER TURN
            {userText}
            """;
    }

    private static string? FindRepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}

internal static class RealCodexRelaySmokeTest
{
    public static async Task RunAsync()
    {
        await using var relay = new RealCodexRelay();
        var messages = new List<JsonDocument>();
        var gate = new object();
        void Capture(object message)
        {
            var document = JsonDocument.Parse(JsonSerializer.Serialize(message));
            lock (gate) messages.Add(document);
        }

        await relay.HandleAsync("{\"type\":\"ready\",\"boot\":1}", Capture);
        await relay.HandleAsync("{\"type\":\"command\",\"command\":\"send_turn\",\"text\":\"Do not use tools or inspect files. Reply with exactly: REAL_CODEX_RELAY_OK\"}", Capture);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        while (!timeout.IsCancellationRequested)
        {
            JsonDocument? handoff;
            lock (gate) handoff = messages.FirstOrDefault(message => EventType(message) == "handoff_created");
            if (handoff is not null)
            {
                var payload = handoff.RootElement.GetProperty("payload");
                if (payload.GetProperty("authorityDecisionCreated").GetBoolean())
                    throw new InvalidOperationException("Real Codex smoke created an AuthorityDecision.");
                var text = payload.GetProperty("text").GetString() ?? string.Empty;
                if (!text.Contains("REAL_CODEX_RELAY_OK", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Unexpected Real Codex response: {text}");
                Console.WriteLine("Real Codex Relay smoke passed: session, transcript, streaming, completion, Skill injection, and non-authoritative Handoff projection.");
                return;
            }

            JsonDocument? error;
            lock (gate) error = messages.LastOrDefault(message => EventType(message) == "error");
            if (error is not null) throw new InvalidOperationException(error.RootElement.GetProperty("payload").GetProperty("message").GetString());
            await Task.Delay(100, timeout.Token);
        }

        throw new TimeoutException("Real Codex Relay smoke did not complete within two minutes.");
    }

    private static string? EventType(JsonDocument document) =>
        document.RootElement.TryGetProperty("eventType", out var eventType) ? eventType.GetString() : null;
}

internal sealed class FakeRelay
{
    private readonly object _gate = new();
    private readonly List<Dictionary<string, object?>> _messages =
    [
        new() { ["id"] = "m-1", ["role"] = "assistant", ["text"] = "This transcript is owned by the Host. Try selecting from the blank area beside this message, then drag across the next one." },
        new() { ["id"] = "m-2", ["role"] = "assistant", ["text"] = "Reload the page after a turn; Assignment, Attempt, session identity, approval correlation, and transcript remain in the relay." }
    ];
    private long _sequence;
    private int _turnNumber;
    private string? _pendingApproval;
    private CancellationTokenSource? _turnCancellation;

    public void Handle(string json, Action<object> send)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var type = root.GetProperty("type").GetString();
        if (type == "ready")
        {
            send(new
            {
                type = "initialize",
                session = new
                {
                    projectId = "project-spike-001",
                    role = "leader",
                    assignmentId = "assignment-spike-relay",
                    attemptId = "attempt-0001",
                    agentSessionId = "fake-session-001",
                    provider = "fake-agent",
                    capabilities = new { streaming = true, approval = true, interrupt = true, steer = true, attachments = true, transcript = true }
                },
                events = new[] { Event("transcript", new { messages = SnapshotMessages() }) }
            });
            return;
        }

        if (type != "command") return;
        var command = root.GetProperty("command").GetString();
        switch (command)
        {
            case "send_turn": StartTurn(root.GetProperty("text").GetString() ?? string.Empty, send); break;
            case "respond_approval": ResolveApproval(root, send); break;
            case "interrupt": Interrupt(send); break;
            case "steer": Steer(root, send); break;
            case "attach": Attach(root, send); break;
            case "reload_transcript": SendTranscript(send); break;
        }
    }

    private void StartTurn(string text, Action<object> send)
    {
        lock (_gate) _turnNumber++;
        var turnId = $"turn-{_turnNumber:0000}";
        var userId = $"user-{_turnNumber:0000}";
        var assistantId = $"assistant-{_turnNumber:0000}";
        lock (_gate) _messages.Add(new() { ["id"] = userId, ["role"] = "user", ["text"] = text });
        Emit(send, "user_message", new { messageId = userId, text });
        Emit(send, "turn_started", new { turnId });
        _turnCancellation?.Cancel();
        _turnCancellation = new CancellationTokenSource();
        _ = RunTurnAsync(turnId, assistantId, text, send, _turnCancellation.Token);
    }

    private async Task RunTurnAsync(string turnId, string assistantId, string text, Action<object> send, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken);
            Emit(send, "assistant_delta", new { messageId = assistantId, text = "Fake Agent is streaming a response. " });
            await Task.Delay(250, cancellationToken);
            Emit(send, "assistant_delta", new { messageId = assistantId, text = "The Host still owns the session and governance state. " });
            await Task.Delay(250, cancellationToken);
            Emit(send, "tool_started", new { name = "fake.inspect_project_world" });
            await Task.Delay(180, cancellationToken);
            Emit(send, "tool_completed", new { name = "fake.inspect_project_world", result = "read-only observation" });
            await Task.Delay(180, cancellationToken);
            lock (_gate)
            {
                _approvalResult = false;
                _pendingApproval = $"approval-{_turnNumber:0000}";
            }
            Emit(send, "approval_requested", new { requestId = _pendingApproval, description = "Allow the Fake Agent to continue this demonstration turn?" });
            var approved = await WaitForApprovalAsync(cancellationToken);
            if (!approved) { Emit(send, "assistant_delta", new { messageId = assistantId, text = "The user denied the request; the turn ended without a project-state mutation." }); }
            else { Emit(send, "assistant_delta", new { messageId = assistantId, text = "Approval received. Final result is returned as a non-authoritative Handoff." }); }
            lock (_gate) _messages.Add(new() { ["id"] = assistantId, ["role"] = "assistant", ["text"] = approved ? "Fake Agent is streaming a response. The Host still owns the session and governance state. Approval received. Final result is returned as a non-authoritative Handoff." : "The user denied the request; the turn ended without a project-state mutation." });
            Emit(send, "turn_completed", new { turnId });
            Emit(send, "handoff_created", new { handoffId = $"handoff-{_turnNumber:0000}", authorityDecisionCreated = false });
        }
        catch (OperationCanceledException) { }
    }

    private async Task<bool> WaitForApprovalAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate) if (_pendingApproval is null) return _approvalResult;
            await Task.Delay(50, cancellationToken);
        }
    }

    private bool _approvalResult;

    private void ResolveApproval(JsonElement root, Action<object> send)
    {
        var requestId = root.GetProperty("requestId").GetString();
        lock (_gate)
        {
            if (!string.Equals(requestId, _pendingApproval, StringComparison.Ordinal)) return;
            _approvalResult = string.Equals(root.GetProperty("option").GetString(), "allow", StringComparison.Ordinal);
            _pendingApproval = null;
        }
        Emit(send, "approval_resolved", new { requestId, option = _approvalResult ? "allow" : "deny" });
    }

    private void Interrupt(Action<object> send)
    {
        _turnCancellation?.Cancel();
        lock (_gate) _pendingApproval = null;
        Emit(send, "turn_interrupted", new { reason = "user_stop" });
    }

    private void Steer(JsonElement root, Action<object> send) =>
        Emit(send, "steer_acknowledged", new { turnId = root.GetProperty("turnId").GetString(), text = root.GetProperty("text").GetString() });

    private void Attach(JsonElement root, Action<object> send)
    {
        var file = root.GetProperty("file");
        var token = $"attachment-token-{Guid.NewGuid():N}";
        Emit(send, "attachment_received", new { token, name = file.GetProperty("name").GetString(), size = file.GetProperty("size").GetInt64(), mediaType = file.GetProperty("type").GetString() });
    }

    private void SendTranscript(Action<object> send) => Emit(send, "transcript", new { messages = SnapshotMessages() });

    private List<Dictionary<string, object?>> SnapshotMessages() { lock (_gate) return _messages.Select(message => new Dictionary<string, object?>(message)).ToList(); }

    private Dictionary<string, object?> Event(string eventType, object payload)
    {
        lock (_gate)
        {
            return new() { ["type"] = "event", ["sequence"] = ++_sequence, ["eventType"] = eventType, ["payload"] = payload };
        }
    }
    private void Emit(Action<object> send, string eventType, object payload) => send(Event(eventType, payload));
}

internal static class FakeRelaySelfTest
{
    public static async Task RunAsync()
    {
        var relay = new FakeRelay();
        var received = new List<JsonDocument>();
        var gate = new object();
        void Capture(object message)
        {
            lock (gate) received.Add(JsonDocument.Parse(JsonSerializer.Serialize(message)));
        }

        relay.Handle("{\"type\":\"ready\",\"boot\":1}", Capture);
        Assert(Find(received, "initialize") is not null, "initialize");
        Assert(HasTranscriptInInitialize(received), "initial transcript");

        relay.Handle("{\"type\":\"command\",\"command\":\"send_turn\",\"text\":\"Run the fake relay proof\"}", Capture);
        await WaitForAsync(received, "approval_requested");
        Assert(Find(received, "assistant_delta") is not null, "streaming delta");
        Assert(Find(received, "tool_started") is not null, "tool started");
        var approval = Find(received, "approval_requested")!.RootElement.GetProperty("payload").GetProperty("requestId").GetString();
        relay.Handle($"{{\"type\":\"command\",\"command\":\"respond_approval\",\"requestId\":\"{approval}\",\"option\":\"allow\"}}", Capture);
        await WaitForAsync(received, "handoff_created");
        Assert(Find(received, "approval_resolved") is not null, "approval round-trip");
        Assert(Find(received, "turn_completed") is not null, "turn completed");
        Assert(Find(received, "handoff_created")!.RootElement.GetProperty("payload").GetProperty("authorityDecisionCreated").GetBoolean() == false, "handoff is non-authoritative");

        relay.Handle("{\"type\":\"command\",\"command\":\"attach\",\"file\":{\"name\":\"proof.png\",\"size\":12,\"type\":\"image/png\"}}", Capture);
        Assert(Find(received, "attachment_received")!.RootElement.GetProperty("payload").GetProperty("token").GetString()!.StartsWith("attachment-token-", StringComparison.Ordinal), "attachment token");

        relay.Handle("{\"type\":\"command\",\"command\":\"send_turn\",\"text\":\"interrupt me\"}", Capture);
        await Task.Delay(80);
        relay.Handle("{\"type\":\"command\",\"command\":\"steer\",\"turnId\":\"turn-0002\",\"text\":\"change direction\"}", Capture);
        relay.Handle("{\"type\":\"command\",\"command\":\"interrupt\",\"turnId\":\"turn-0002\"}", Capture);
        Assert(Find(received, "steer_acknowledged") is not null, "steer");
        Assert(Find(received, "turn_interrupted") is not null, "interrupt");

        var beforeReload = CountInitialTranscripts(received);
        relay.Handle("{\"type\":\"ready\",\"boot\":2}", Capture);
        Assert(CountInitialTranscripts(received) > beforeReload, "restart transcript continuity");
        Console.WriteLine("Fake Relay self-test passed: protocol, approval, stream, attachment, steer, interrupt, handoff, and restart continuity.");
    }

    private static async Task WaitForAsync(List<JsonDocument> received, string eventType)
    {
        for (var i = 0; i < 80; i++)
        {
            if (Find(received, eventType) is not null) return;
            await Task.Delay(50);
        }
        throw new InvalidOperationException($"Timed out waiting for {eventType}.");
    }

    private static JsonDocument? Find(List<JsonDocument> received, string eventType)
    {
        lock (received)
        {
            return received.FirstOrDefault(message => message.RootElement.TryGetProperty("eventType", out var value) && value.GetString() == eventType)
                ?? received.FirstOrDefault(message => message.RootElement.TryGetProperty("type", out var type) && type.GetString() == eventType);
        }
    }

    private static int CountEvents(List<JsonDocument> received, string eventType) => received.Count(message => message.RootElement.TryGetProperty("eventType", out var value) && value.GetString() == eventType);
    private static bool HasTranscriptInInitialize(List<JsonDocument> received) => CountInitialTranscripts(received) > 0;
    private static int CountInitialTranscripts(List<JsonDocument> received) => received.Count(message =>
        message.RootElement.TryGetProperty("type", out var type) && type.GetString() == "initialize" &&
        message.RootElement.TryGetProperty("events", out var events) && events.EnumerateArray().Any(item => item.TryGetProperty("eventType", out var eventType) && eventType.GetString() == "transcript"));
    private static void Assert(bool condition, string label) { if (!condition) throw new InvalidOperationException($"Self-test failed: {label}"); }
}
