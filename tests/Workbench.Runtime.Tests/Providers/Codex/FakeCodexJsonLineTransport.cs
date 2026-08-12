using System.Threading.Channels;
using Workbench.Runtime.Providers.Codex;

namespace Workbench.Runtime.Tests.Providers.Codex;

internal sealed class FakeCodexJsonLineTransport : ICodexJsonLineTransport
{
    private readonly Channel<string> _clientLines = Channel.CreateUnbounded<string>();
    private readonly Channel<string> _serverLines = Channel.CreateUnbounded<string>();

    public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default) =>
        _clientLines.Writer.WriteAsync(line, cancellationToken);

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _serverLines.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public ValueTask<string> ReadClientLineAsync(CancellationToken cancellationToken = default) =>
        _clientLines.Reader.ReadAsync(cancellationToken);

    public ValueTask SendServerLineAsync(string line, CancellationToken cancellationToken = default) =>
        _serverLines.Writer.WriteAsync(line, cancellationToken);

    public void CompleteServerOutput() => _serverLines.Writer.TryComplete();

    public ValueTask DisposeAsync()
    {
        _clientLines.Writer.TryComplete();
        _serverLines.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
