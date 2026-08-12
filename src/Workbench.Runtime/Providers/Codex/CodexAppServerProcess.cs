using System.Diagnostics;
using System.Text;

namespace Workbench.Runtime.Providers.Codex;

internal sealed class CodexAppServerProcess : ICodexJsonLineTransport
{
    private readonly Process _process;
    private readonly Task<string> _stderrTask;
    private int _disposed;

    private CodexAppServerProcess(Process process)
    {
        _process = process;
        _stderrTask = process.StandardError.ReadToEndAsync();
    }

    public static CodexAppServerProcess Start(CodexAppServerOptions options)
    {
        var process = new Process { StartInfo = CreateStartInfo(options), EnableRaisingEvents = true };

        try
        {
            if (!process.Start())
            {
                throw new CodexProtocolException("Codex app-server process did not start.");
            }

            return new CodexAppServerProcess(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(CodexAppServerOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.ExecutablePath,
            WorkingDirectory = options.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in options.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default) =>
        await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);

    public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
    {
        if (_process.HasExited)
        {
            var stderr = await _stderrTask.ConfigureAwait(false);
            throw new CodexProtocolException(
                $"Codex app-server exited with code {_process.ExitCode}. {stderr}".Trim());
        }

        await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _process.StandardInput.Close();
        }
        catch
        {
        }

        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _process.WaitForExitAsync(shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }

        await _stderrTask.ConfigureAwait(false);
        _process.Dispose();
    }
}
