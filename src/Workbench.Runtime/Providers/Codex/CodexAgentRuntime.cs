using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Runtime;

namespace Workbench.Runtime.Providers.Codex;

public sealed class CodexAgentRuntime : IAgentRuntime, IAsyncDisposable
{
    private static readonly ProviderId CodexProviderId = new("codex");
    private readonly CodexProtocolClient _client;
    private readonly CodexAppServerOptions _options;
    private readonly Dictionary<AgentSessionId, string> _activeTurns = [];
    private readonly Lock _activeTurnsLock = new();
    private int _disposed;

    private CodexAgentRuntime(
        CodexProtocolClient client,
        CodexAppServerOptions options,
        ProviderAccountId accountId)
    {
        _client = client;
        _options = options;
        Provider = new ProviderDescriptor(CodexProviderId, "Codex");
        Account = new ProviderAccountSummary(accountId, CodexProviderId, "Local Codex Account", true);
    }

    public ProviderDescriptor Provider { get; }

    public ProviderAccountSummary Account { get; }

    public AgentCapability Capabilities { get; } =
        AgentCapability.PersistentSession |
        AgentCapability.Resume |
        AgentCapability.StructuredEvents |
        AgentCapability.Stop |
        AgentCapability.Transcript |
        AgentCapability.ParallelSessions;

    public static async Task<CodexAgentRuntime> ConnectAsync(
        CodexAppServerOptions options,
        ProviderAccountId accountId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var process = CodexAppServerProcess.Start(options);
        var client = new CodexProtocolClient(process);

        try
        {
            return await CreateForProtocolAsync(client, options, accountId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static async Task<CodexAgentRuntime> CreateForProtocolAsync(
        CodexProtocolClient client,
        CodexAppServerOptions options,
        ProviderAccountId accountId,
        CancellationToken cancellationToken = default)
    {
        var runtime = new CodexAgentRuntime(client, options, accountId);
        await runtime.RequestAsync(
            "initialize",
            new
            {
                clientInfo = new
                {
                    name = "ai_game_workbench",
                    title = "AI Game Workbench",
                    version = "0.1.0-dev"
                }
            },
            cancellationToken).ConfigureAwait(false);
        await client.SendNotificationAsync("initialized", cancellationToken: cancellationToken).ConfigureAwait(false);
        return runtime;
    }

    public async Task<IReadOnlyList<ModelProfile>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var models = new List<ModelProfile>();
        string? cursor = null;

        do
        {
            var result = await RequestAsync(
                "model/list",
                new { cursor, limit = 100, includeHidden = false },
                cancellationToken).ConfigureAwait(false);
            models.AddRange(CodexRuntimeMapper.MapModels(result));
            cursor = result.TryGetProperty("nextCursor", out var nextCursor) && nextCursor.ValueKind != JsonValueKind.Null
                ? nextCursor.GetString()
                : null;
        }
        while (cursor is not null);

        return models;
    }

    public async Task<AgentSession> CreateSessionAsync(
        CreateAgentSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.AccountId != Account.Id)
        {
            throw new InvalidOperationException("The session account does not match this Codex runtime.");
        }

        var result = await RequestAsync(
            "thread/start",
            new
            {
                model = request.ModelId,
                cwd = _options.WorkingDirectory,
                sandbox = "read-only",
                approvalPolicy = "never"
            },
            cancellationToken).ConfigureAwait(false);
        return CodexRuntimeMapper.MapSession(result, Account.Id, request.ModelId);
    }

    public async Task<AgentSession> ResumeSessionAsync(
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        var result = await RequestAsync(
            "thread/resume",
            new
            {
                threadId = session.ExternalSessionId,
                model = session.ModelId,
                cwd = _options.WorkingDirectory,
                sandbox = "read-only",
                approvalPolicy = "never"
            },
            cancellationToken).ConfigureAwait(false);
        var mapped = CodexRuntimeMapper.MapSession(result, Account.Id, session.ModelId);
        return mapped with { Id = session.Id, CreatedAt = session.CreatedAt };
    }

    public async IAsyncEnumerable<AgentEvent> SendAsync(
        AgentSession session,
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        var notifications = Channel.CreateUnbounded<CodexProtocolMessage>();
        void Receive(CodexProtocolMessage message)
        {
            if (BelongsToThread(message.Params, session.ExternalSessionId!))
            {
                notifications.Writer.TryWrite(message);
            }
        }

        _client.NotificationReceived += Receive;
        var finalText = new StringBuilder();

        try
        {
            var result = await RequestAsync(
                "turn/start",
                new
                {
                    threadId = session.ExternalSessionId,
                    input = new[] { new { type = "text", text = request.Text } },
                    cwd = _options.WorkingDirectory,
                    approvalPolicy = "never",
                    sandboxPolicy = new { type = "readOnly", networkAccess = false }
                },
                cancellationToken).ConfigureAwait(false);
            var turnId = result.GetProperty("turn").GetProperty("id").GetString()
                ?? throw new CodexProtocolException("turn/start returned no turn id.");
            lock (_activeTurnsLock)
            {
                _activeTurns[session.Id] = turnId;
            }

            while (await notifications.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (notifications.Reader.TryRead(out var notification))
                {
                    if (notification.Method == "item/agentMessage/delta")
                    {
                        var text = notification.Params.GetProperty("delta").GetString() ?? string.Empty;
                        finalText.Append(text);
                    }
                    else if (notification.Method == "item/completed" && finalText.Length == 0)
                    {
                        TryCaptureCompletedAgentMessage(notification.Params, finalText);
                    }

                    var mapped = CodexRuntimeMapper.MapNotification(
                        notification.Method,
                        notification.Params,
                        session.Id,
                        finalText.ToString());
                    if (mapped is not null)
                    {
                        yield return mapped;
                    }

                    if (mapped is AgentTurnCompleted)
                    {
                        yield break;
                    }
                }
            }
        }
        finally
        {
            _client.NotificationReceived -= Receive;
            lock (_activeTurnsLock)
            {
                _activeTurns.Remove(session.Id);
            }
        }
    }

    public async Task StopAsync(AgentSession session, CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        string? turnId;
        lock (_activeTurnsLock)
        {
            _activeTurns.TryGetValue(session.Id, out turnId);
        }

        if (turnId is null)
        {
            return;
        }

        await RequestAsync(
            "turn/interrupt",
            new { threadId = session.ExternalSessionId, turnId },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AgentSessionStatus> GetStatusAsync(
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        var result = await ReadThreadAsync(session, includeTurns: false, cancellationToken).ConfigureAwait(false);
        var status = result.GetProperty("thread").GetProperty("status");
        if (status.ValueKind == JsonValueKind.String)
        {
            return status.GetString() switch
            {
                "active" => AgentSessionStatus.Running,
                "systemError" => AgentSessionStatus.Failed,
                "notLoaded" => AgentSessionStatus.Created,
                _ => AgentSessionStatus.Ready
            };
        }

        return status.TryGetProperty("type", out var type) && type.GetString() == "systemError"
            ? AgentSessionStatus.Failed
            : AgentSessionStatus.Ready;
    }

    public async Task<IReadOnlyList<AgentEvent>> GetTranscriptAsync(
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        var result = await ReadThreadAsync(session, includeTurns: true, cancellationToken).ConfigureAwait(false);
        return CodexRuntimeMapper.MapTranscript(result);
    }

    internal Task<JsonElement> ReadThreadAsync(
        AgentSession session,
        bool includeTurns,
        CancellationToken cancellationToken = default)
    {
        ValidateSession(session);
        return RequestAsync(
            "thread/read",
            new { threadId = session.ExternalSessionId, includeTurns },
            cancellationToken);
    }

    private async Task<JsonElement> RequestAsync(
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.OperationTimeout);
        try
        {
            return await _client.SendRequestAsync(method, parameters, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Codex app-server method '{method}' exceeded {_options.OperationTimeout}.");
        }
    }

    private void ValidateSession(AgentSession session)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(session);

        if (session.AccountId != Account.Id || session.ProviderId != Provider.Id)
        {
            throw new InvalidOperationException("The session does not belong to this Codex runtime.");
        }

        if (string.IsNullOrWhiteSpace(session.ExternalSessionId))
        {
            throw new InvalidOperationException("The Codex session has no external thread id.");
        }
    }

    private static bool BelongsToThread(JsonElement parameters, string threadId) =>
        parameters.TryGetProperty("threadId", out var eventThreadId) &&
        string.Equals(eventThreadId.GetString(), threadId, StringComparison.Ordinal);

    private static void TryCaptureCompletedAgentMessage(JsonElement parameters, StringBuilder finalText)
    {
        if (!parameters.TryGetProperty("item", out var item) ||
            !item.TryGetProperty("type", out var type) ||
            type.GetString() != "agentMessage" ||
            !item.TryGetProperty("text", out var text))
        {
            return;
        }

        finalText.Append(text.GetString());
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }
}
