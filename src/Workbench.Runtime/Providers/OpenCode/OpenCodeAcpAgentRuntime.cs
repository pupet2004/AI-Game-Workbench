using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Runtime;

namespace Workbench.Runtime.Providers.OpenCode;

public sealed class OpenCodeAcpAgentRuntime : IAgentRuntime, IAsyncDisposable
{
    private static readonly ProviderId AgentProviderId = new("opencode");
    private readonly OpenCodeAcpClient _client;
    private readonly OpenCodeAcpOptions _options;
    private readonly ConcurrentDictionary<string, TurnBuffer> _turns = new(StringComparer.Ordinal);
    private IReadOnlyList<ModelProfile>? _models;

    private OpenCodeAcpAgentRuntime(OpenCodeAcpClient client, OpenCodeAcpOptions options, ProviderAccountId accountId)
    {
        _client = client;
        _options = options;
        Provider = new ProviderDescriptor(AgentProviderId, "OpenCode");
        Account = new ProviderAccountSummary(accountId, AgentProviderId, "Local OpenCode Account", true);
        _client.NotificationReceived += OnNotification;
        _client.ServerRequestReceived += OnServerRequest;
    }

    public string RuntimeKind => "opencode-acp";
    public ProviderDescriptor Provider { get; }
    public ProviderAccountSummary Account { get; }
    public AgentCapability Capabilities { get; } =
        AgentCapability.PersistentSession | AgentCapability.Resume | AgentCapability.StructuredEvents |
        AgentCapability.Stop | AgentCapability.Transcript | AgentCapability.ParallelSessions;

    public static async Task<OpenCodeAcpAgentRuntime> ConnectAsync(
        OpenCodeAcpOptions options,
        ProviderAccountId accountId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var client = await OpenCodeAcpClient.StartAsync(options, cancellationToken).ConfigureAwait(false);
        return new OpenCodeAcpAgentRuntime(client, options, accountId);
    }

    public async Task<IReadOnlyList<ModelProfile>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        if (_models is not null) return _models;
        var response = await _client.SendRequestAsync("session/new", new { cwd = _options.WorkingDirectory, mcpServers = Array.Empty<object>() }, cancellationToken).ConfigureAwait(false);
        var models = new List<ModelProfile>();
        if (response.TryGetProperty("configOptions", out var options) && options.ValueKind == JsonValueKind.Array)
        {
            foreach (var option in options.EnumerateArray())
            {
                if (!option.TryGetProperty("id", out var id) || id.GetString() != "model" || !option.TryGetProperty("options", out var values)) continue;
                foreach (var value in values.EnumerateArray())
                {
                    var modelId = value.TryGetProperty("value", out var modelValue) ? modelValue.GetString() : null;
                    if (string.IsNullOrWhiteSpace(modelId)) continue;
                    var provider = modelId.Split('/', 2) is [var providerId, _] ? providerId : "opencode";
                    var display = value.TryGetProperty("name", out var name) ? name.GetString() : modelId;
                    models.Add(new ModelProfile(new ProviderId(provider!), modelId, display ?? modelId, AgentCapability.StructuredEvents));
                }
            }
        }
        return _models = models;
    }

    public async Task<AgentSession> CreateSessionAsync(CreateAgentSessionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.AccountId != Account.Id) throw new InvalidOperationException("The session account does not match this OpenCode runtime.");
        var response = await _client.SendRequestAsync("session/new", new { cwd = request.WorkingDirectory ?? _options.WorkingDirectory, mcpServers = Array.Empty<object>() }, cancellationToken).ConfigureAwait(false);
        var externalId = response.GetProperty("sessionId").GetString() ?? throw new InvalidOperationException("OpenCode did not return a session id.");
        await SelectModelAsync(externalId, request.ModelId, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        return new AgentSession(AgentSessionId.New(), Account.Id, Provider.Id, request.ModelId, request.WorkingDirectory, externalId, AgentSessionStatus.Ready, now, now);
    }

    public async Task<AgentSession> ResumeSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
    {
        if (session.AccountId != Account.Id || session.ProviderId != Provider.Id || string.IsNullOrWhiteSpace(session.ExternalSessionId))
            throw new InvalidOperationException("The session does not belong to this OpenCode runtime.");
        await _client.SendRequestAsync("session/load", new { sessionId = session.ExternalSessionId, cwd = session.WorkingDirectory ?? _options.WorkingDirectory, mcpServers = Array.Empty<object>() }, cancellationToken).ConfigureAwait(false);
        return session with { Status = AgentSessionStatus.Ready, UpdatedAt = DateTimeOffset.UtcNow };
    }

    public async IAsyncEnumerable<AgentEvent> SendAsync(AgentSession session, AgentRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var externalId = session.ExternalSessionId ?? throw new InvalidOperationException("OpenCode sessions require an external session id.");
        var turn = new TurnBuffer();
        if (!_turns.TryAdd(externalId, turn)) throw new InvalidOperationException("An OpenCode turn is already active for this session.");
        try
        {
            var response = await _client.SendRequestAsync("session/prompt", new { sessionId = externalId, prompt = new[] { new { type = "text", text = BuildPromptText(request) } } }, cancellationToken).ConfigureAwait(false);
            foreach (var item in turn.Events) yield return item;
            var finalText = turn.Text.ToString().Trim();
            if (response.TryGetProperty("result", out var result) && result.TryGetProperty("text", out var resultText)) finalText = resultText.GetString() ?? finalText;
            yield return new AgentTurnCompleted(new AgentResult(session.Id, AgentSessionStatus.Completed, string.IsNullOrWhiteSpace(finalText) ? null : finalText, null), DateTimeOffset.UtcNow);
        }
        finally { _turns.TryRemove(externalId, out _); }
    }

    internal static string BuildPromptText(AgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OutputSchema))
            return request.Text;

        // This ACP path has no native output-schema field. Keep the contract in
        // the prompt; callers must still parse and validate the returned JSON.
        return $"""
            {request.Text}

            WORKBENCH RESPONSE FORMAT
            Return only valid JSON matching the following JSON Schema.
            Do not include Markdown fences or prose outside the JSON.
            This output format does not authorize execution or project acceptance.
            JSON Schema:
            {request.OutputSchema}
            """;
    }

    public Task RespondToApprovalAsync(AgentSession session, AgentApprovalDecision decision, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("OpenCode ACP approval decisions are not wired into Workbench yet.");

    public async Task StopAsync(AgentSession session, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(session.ExternalSessionId))
        {
            try { await _client.SendRequestAsync("session/cancel", new { sessionId = session.ExternalSessionId }, cancellationToken).ConfigureAwait(false); }
            catch (OpenCodeAcpException) { }
        }
    }

    public Task<AgentSessionStatus> GetStatusAsync(AgentSession session, CancellationToken cancellationToken = default) => Task.FromResult(session.Status);
    public Task<IReadOnlyList<AgentEvent>> GetTranscriptAsync(AgentSession session, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AgentEvent>>([]);
    public ValueTask DisposeAsync() => _client.DisposeAsync();

    private async Task SelectModelAsync(string sessionId, string modelId, CancellationToken cancellationToken)
    {
        await _client.SendRequestAsync("session/set_config_option", new { sessionId, configId = "model", value = modelId }, cancellationToken).ConfigureAwait(false);
    }

    private void OnNotification(string method, JsonElement parameters)
    {
        if (!string.Equals(method, "session/update", StringComparison.Ordinal) || !parameters.TryGetProperty("sessionId", out var id)) return;
        var sessionId = id.GetString();
        if (string.IsNullOrWhiteSpace(sessionId) || !_turns.TryGetValue(sessionId, out var turn)) return;
        var update = parameters.TryGetProperty("update", out var value) ? value : parameters;
        var kind = update.TryGetProperty("sessionUpdate", out var type) ? type.GetString() : update.TryGetProperty("type", out var type2) ? type2.GetString() : null;
        if (kind is not null && kind.Contains("agent_message", StringComparison.OrdinalIgnoreCase))
        {
            var text = FindText(update);
            if (!string.IsNullOrWhiteSpace(text)) { turn.Text.Append(text); turn.Events.Add(new AgentTextDelta(text, DateTimeOffset.UtcNow)); }
        }
        else if (kind is not null && kind.Contains("tool", StringComparison.OrdinalIgnoreCase))
        {
            turn.Events.Add(new AgentToolEvent("opencode", update.GetRawText(), DateTimeOffset.UtcNow));
        }
    }

    private async void OnServerRequest(JsonElement id, string method, JsonElement parameters)
    {
        try
        {
            if (string.Equals(method, "session/request_permission", StringComparison.Ordinal))
            {
                var sessionId = parameters.TryGetProperty("sessionId", out var session) ? session.GetString() : null;
                if (!string.IsNullOrWhiteSpace(sessionId) && _turns.TryGetValue(sessionId, out var turn))
                {
                    var options = parameters.TryGetProperty("options", out var values) && values.ValueKind == JsonValueKind.Array
                        ? values.EnumerateArray()
                            .Select(value => new AgentApprovalOption(
                                value.TryGetProperty("optionId", out var optionId) ? optionId.GetString() ?? "cancel" : "cancel",
                                value.TryGetProperty("name", out var name) ? name.GetString() ?? "Permission requested" : "Permission requested"))
                            .ToArray()
                        : [new AgentApprovalOption("cancel", "Permission requested")];
                    turn.Events.Add(new AgentApprovalRequested(
                        new AgentApprovalRequestId(Guid.NewGuid()),
                        AgentSessionId.New(),
                        "OpenCode requested permission for a provider action.",
                        options,
                        DateTimeOffset.UtcNow));
                }

                await _client.SendResponseAsync(id, new { outcome = new { outcome = "cancelled" } }).ConfigureAwait(false);
                return;
            }

            await _client.SendErrorAsync(id, -32601, "Workbench does not support this ACP client request.").ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static string? FindText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) return text.GetString();
            foreach (var property in element.EnumerateObject()) { var found = FindText(property.Value); if (!string.IsNullOrWhiteSpace(found)) return found; }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        { foreach (var item in element.EnumerateArray()) { var found = FindText(item); if (!string.IsNullOrWhiteSpace(found)) return found; } }
        return null;
    }

    private sealed class TurnBuffer
    {
        public StringBuilder Text { get; } = new();
        public List<AgentEvent> Events { get; } = [];
    }
}

internal sealed class OpenCodeAcpClient : IAsyncDisposable
{
    private readonly Process _process;
    private readonly OpenCodeAcpOptions _options;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _reader;
    private int _nextId;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    public event Action<string, JsonElement>? NotificationReceived;
    public event Action<JsonElement, string, JsonElement>? ServerRequestReceived;

    private OpenCodeAcpClient(Process process, OpenCodeAcpOptions options) { _process = process; _options = options; _reader = ReadLoopAsync(); }

    public static async Task<OpenCodeAcpClient> StartAsync(OpenCodeAcpOptions options, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo { FileName = options.ExecutablePath, WorkingDirectory = options.WorkingDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("acp");
        var process = new Process { StartInfo = info };
        if (!process.Start()) throw new OpenCodeAcpException("OpenCode ACP process did not start.");
        var client = new OpenCodeAcpClient(process, options);
        try
        {
            await client.SendRequestAsync("initialize", new { protocolVersion = 1, clientCapabilities = new { }, clientInfo = new { name = "ai_game_workbench", version = "0.1.0" } }, cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch { await client.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async Task<JsonElement> SendRequestAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId); var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously); _pending[id] = tcs;
        var line = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });
        await WriteLineAsync(line, cancellationToken).ConfigureAwait(false);
        try { return await tcs.Task.WaitAsync(_options.RequestTimeout, cancellationToken).ConfigureAwait(false); } finally { _pending.TryRemove(id, out _); }
    }

    public Task SendResponseAsync(JsonElement id, object result, CancellationToken cancellationToken = default) =>
        WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), cancellationToken);

    public Task SendErrorAsync(JsonElement id, int code, string message, CancellationToken cancellationToken = default) =>
        WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code, message } }), cancellationToken);

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var line = await _process.StandardOutput.ReadLineAsync(_shutdown.Token).ConfigureAwait(false); if (line is null) break;
                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(line);
                }
                catch (JsonException)
                {
                    // OpenCode may emit a malformed skill/command notification while still returning valid RPC responses.
                    // Ignore that non-authoritative notification and keep the request channel alive.
                    continue;
                }

                using (document)
                {
                    var root = document.RootElement;
                if (root.TryGetProperty("method", out var method))
                {
                    var parameters = root.TryGetProperty("params", out var p) ? p.Clone() : default;
                    if (root.TryGetProperty("id", out var serverId)) ServerRequestReceived?.Invoke(serverId.Clone(), method.GetString() ?? "", parameters);
                    else NotificationReceived?.Invoke(method.GetString() ?? "", parameters);
                    continue;
                }
                    if (root.TryGetProperty("id", out var id) && id.TryGetInt32(out var numeric) && _pending.TryGetValue(numeric, out var pending))
                    {
                        if (root.TryGetProperty("error", out var error)) pending.TrySetException(new OpenCodeAcpException(error.GetRawText()));
                        else if (root.TryGetProperty("result", out var result)) pending.TrySetResult(result.Clone());
                        else pending.TrySetException(new OpenCodeAcpException("OpenCode ACP response had no result."));
                    }
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { foreach (var pending in _pending.Values) pending.TrySetException(exception); }
    }

    private async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        try { _process.StandardInput.Close(); } catch { }
        if (!_process.HasExited)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                if (!_process.HasExited) { _process.Kill(true); await _process.WaitForExitAsync().ConfigureAwait(false); }
            }
        }
        try { await _reader.ConfigureAwait(false); } catch { }
        _process.Dispose(); _shutdown.Dispose(); _writeLock.Dispose();
    }
}

internal sealed class OpenCodeAcpException(string message) : InvalidOperationException(message);
