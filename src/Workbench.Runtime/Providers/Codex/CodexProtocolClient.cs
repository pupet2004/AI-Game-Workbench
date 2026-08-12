using System.Collections.Concurrent;
using System.Text.Json;

namespace Workbench.Runtime.Providers.Codex;

internal sealed class CodexProtocolClient : IAsyncDisposable
{
    private readonly ICodexJsonLineTransport _transport;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _readerTask;
    private long _nextRequestId;
    private int _disposed;

    public CodexProtocolClient(ICodexJsonLineTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _readerTask = ReadLoopAsync();
    }

    public event Action<CodexProtocolMessage>? NotificationReceived;

    public event Action<CodexServerRequest>? ServerRequestReceived;

    public async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException($"Request id '{id}' is already pending.");
        }

        try
        {
            var line = JsonSerializer.Serialize(new { id, method, @params = parameters });
            await _transport.WriteLineAsync(line, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public ValueTask SendNotificationAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        var line = parameters is null
            ? JsonSerializer.Serialize(new { method })
            : JsonSerializer.Serialize(new { method, @params = parameters });
        return _transport.WriteLineAsync(line, cancellationToken);
    }

    private async Task ReadLoopAsync()
    {
        Exception? failure = null;

        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var line = await _transport.ReadLineAsync(_shutdown.Token).ConfigureAwait(false);
                if (line is null)
                {
                    throw new CodexProtocolException("Codex app-server process exited while protocol requests were pending.");
                }

                Dispatch(line);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (JsonException exception)
        {
            failure = new CodexProtocolException("Codex app-server returned malformed JSON.", exception);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            if (failure is not null)
            {
                FailPending(failure);
            }
        }
    }

    private void Dispatch(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        var hasMethod = root.TryGetProperty("method", out var methodElement);
        var hasId = root.TryGetProperty("id", out var idElement);

        if (hasMethod)
        {
            var method = methodElement.GetString()
                ?? throw new CodexProtocolException("Codex protocol message method was null.");
            var parameters = root.TryGetProperty("params", out var paramsElement)
                ? paramsElement.Clone()
                : EmptyObject();

            if (hasId)
            {
                ServerRequestReceived?.Invoke(new CodexServerRequest(idElement.Clone(), method, parameters));
            }
            else
            {
                NotificationReceived?.Invoke(new CodexProtocolMessage(method, parameters));
            }

            return;
        }

        if (!hasId || !idElement.TryGetInt64(out var id) || !_pending.TryGetValue(id, out var completion))
        {
            return;
        }

        if (root.TryGetProperty("error", out var error))
        {
            completion.TrySetException(new CodexProtocolException($"Codex request '{id}' failed: {error.GetRawText()}"));
            return;
        }

        if (!root.TryGetProperty("result", out var result))
        {
            completion.TrySetException(new CodexProtocolException($"Codex response '{id}' had no result."));
            return;
        }

        completion.TrySetResult(result.Clone());
    }

    private void FailPending(Exception exception)
    {
        foreach (var completion in _pending.Values)
        {
            completion.TrySetException(exception);
        }
    }

    private static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();
        FailPending(new ObjectDisposedException(nameof(CodexProtocolClient)));
        await _transport.DisposeAsync().ConfigureAwait(false);

        try
        {
            await _readerTask.ConfigureAwait(false);
        }
        catch
        {
        }

        _shutdown.Dispose();
    }
}
