using System.IO.Pipes;
using System.Text;

namespace Workbench.App.SingleInstance;

/// <summary>Owns the per-user-session Workbench host and forwards later launches.</summary>
public sealed class SingleInstanceCoordinator : IDisposable
{
    public const string DefaultMutexName = "AI.Game.Workbench.ApplicationHost.v1";
    public const string DefaultPipeName = "AI.Game.Workbench.ApplicationHost.Activation.v1";
    private const string ActivateMainWindow = "ActivateMainWindow";
    private static readonly Encoding WireEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _shutdown = new();
    private bool _ownsMutex;
    private bool _disposed;

    private SingleInstanceCoordinator(Mutex mutex, bool ownsMutex, string pipeName)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
        _pipeName = pipeName;
    }

    public static bool TryAcquirePrimary(out SingleInstanceCoordinator? coordinator)
        => TryAcquirePrimary(DefaultMutexName, DefaultPipeName, out coordinator);

    internal static bool TryAcquirePrimary(string mutexName, string pipeName, out SingleInstanceCoordinator? coordinator)
    {
        var mutex = new Mutex(false, mutexName, out _);
        var ownsMutex = false;
        try { ownsMutex = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { ownsMutex = true; }
        if (ownsMutex)
        {
            coordinator = new SingleInstanceCoordinator(mutex, true, pipeName);
            return true;
        }

        mutex.Dispose();
        coordinator = null;
        if (!TryActivateExisting(pipeName))
        {
            Console.Error.WriteLine("Another Workbench instance owns the application host, but activation failed.");
        }
        return false;
    }

    internal static bool TryActivateExisting(string pipeName, int attempts = 20, int delayMilliseconds = 100)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                client.Connect(500);
                using var writer = new StreamWriter(client, WireEncoding, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(client, WireEncoding, leaveOpen: true);
                writer.WriteLine(ActivateMainWindow);
                return string.Equals(reader.ReadLine(), "OK", StringComparison.Ordinal);
            }
            catch (IOException) when (attempt + 1 < attempts) { Thread.Sleep(delayMilliseconds); }
            catch (TimeoutException) when (attempt + 1 < attempts) { Thread.Sleep(delayMilliseconds); }
        }

        return false;
    }

    public void Start(Func<Task> activateMainWindow)
    {
        ArgumentNullException.ThrowIfNull(activateMainWindow);
        _ = ListenAsync(activateMainWindow);
    }

    private async Task ListenAsync(Func<Task> activateMainWindow)
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);
                using var reader = new StreamReader(server, WireEncoding, leaveOpen: true);
                using var writer = new StreamWriter(server, WireEncoding, leaveOpen: true) { AutoFlush = true };
                var command = await reader.ReadLineAsync(_shutdown.Token).ConfigureAwait(false);
                if (string.Equals(command, ActivateMainWindow, StringComparison.Ordinal))
                {
                    await activateMainWindow().ConfigureAwait(false);
                    await writer.WriteLineAsync("OK").ConfigureAwait(false);
                }
                else
                {
                    await writer.WriteLineAsync("ERROR").ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { return; }
            catch (IOException) when (!_shutdown.IsCancellationRequested) { await Task.Delay(100, _shutdown.Token).ConfigureAwait(false); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        if (_ownsMutex)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
            _ownsMutex = false;
        }
        _mutex.Dispose();
        _shutdown.Dispose();
    }
}
