using System.Diagnostics;

namespace Workbench.App.Worker;

public interface IAgentInteractiveSessionLauncher
{
    bool CanOpen(WorkerSessionRecord worker);

    Task<InteractiveSessionOpenResult> OpenAsync(
        WorkerSessionRecord worker,
        CancellationToken cancellationToken = default);
}

public sealed record InteractiveSessionOpenResult(bool Succeeded, string? Error)
{
    public static InteractiveSessionOpenResult Success() => new(true, null);

    public static InteractiveSessionOpenResult Failure(string error) => new(false, error);
}

public sealed class CodexInteractiveSessionLauncher : IAgentInteractiveSessionLauncher
{
    private readonly Action<ProcessStartInfo> _startProcess;

    public CodexInteractiveSessionLauncher()
        : this(startInfo => Process.Start(startInfo))
    {
    }

    internal CodexInteractiveSessionLauncher(Action<ProcessStartInfo> startProcess)
    {
        _startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
    }

    public bool CanOpen(WorkerSessionRecord worker) =>
        string.Equals(worker.Session.ProviderId.Value, "codex", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(worker.Session.ExternalSessionId) &&
        !string.IsNullOrWhiteSpace(worker.Session.WorkingDirectory);

    public Task<InteractiveSessionOpenResult> OpenAsync(
        WorkerSessionRecord worker,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(worker.Session.ProviderId.Value, "codex", StringComparison.Ordinal))
        {
            return Task.FromResult(InteractiveSessionOpenResult.Failure(
                "This Worker runtime does not support native Codex sessions."));
        }

        if (string.IsNullOrWhiteSpace(worker.Session.ExternalSessionId) ||
            string.IsNullOrWhiteSpace(worker.Session.WorkingDirectory))
        {
            return Task.FromResult(InteractiveSessionOpenResult.Failure(
                "This Worker has no resumable Codex session."));
        }

        try
        {
            _startProcess(CreateStartInfo(worker));
            return Task.FromResult(InteractiveSessionOpenResult.Success());
        }
        catch (Exception)
        {
            return Task.FromResult(InteractiveSessionOpenResult.Failure(
                "Could not open the native Codex window."));
        }
    }

    internal static ProcessStartInfo CreateStartInfo(WorkerSessionRecord worker)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "wt.exe",
            UseShellExecute = true
        };
        startInfo.ArgumentList.Add("-w");
        startInfo.ArgumentList.Add("new");
        startInfo.ArgumentList.Add("--size");
        startInfo.ArgumentList.Add("120,42");
        startInfo.ArgumentList.Add("--title");
        startInfo.ArgumentList.Add($"[Worker] {worker.TaskTitle} - Codex");
        startInfo.ArgumentList.Add("--suppressApplicationTitle");
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(worker.Session.WorkingDirectory!);
        startInfo.ArgumentList.Add("cmd.exe");
        startInfo.ArgumentList.Add("/k");
        startInfo.ArgumentList.Add("codex");
        startInfo.ArgumentList.Add("resume");
        startInfo.ArgumentList.Add(worker.Session.ExternalSessionId!);
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(worker.Session.WorkingDirectory!);
        return startInfo;
    }
}
