namespace Workbench.Runtime.Providers.Codex;

internal interface ICodexJsonLineTransport : IAsyncDisposable
{
    ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default);

    ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default);
}
