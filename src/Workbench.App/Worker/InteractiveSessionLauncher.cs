using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Workbench.Runtime.Agents;

namespace Workbench.App.Worker;

public interface IAgentInteractiveSessionLauncher
{
    bool CanOpen(WorkerSessionRecord worker);

    Task<InteractiveSessionOpenResult> OpenAsync(
        WorkerSessionRecord worker,
        CancellationToken cancellationToken = default);
}

public sealed record InteractiveSessionOpenResult(bool Succeeded, string? Error, Task? Completed = null, int? ProcessId = null)
{
    public static InteractiveSessionOpenResult Success(Task? completed = null, int? processId = null) => new(true, null, completed, processId);

    public static InteractiveSessionOpenResult Failure(string error) => new(false, error);
}

public sealed class CodexInteractiveSessionLauncher : IAgentInteractiveSessionLauncher
{
    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private readonly Func<Workbench.Runtime.Providers.ProviderAccountId, CancellationToken, Task<bool>>? _releaseRuntime;

    public CodexInteractiveSessionLauncher()
        : this(startInfo => Process.Start(startInfo), null)
    {
    }

    public CodexInteractiveSessionLauncher(
        Func<Workbench.Runtime.Providers.ProviderAccountId, CancellationToken, Task<bool>>? releaseRuntime)
        : this(startInfo => Process.Start(startInfo), releaseRuntime)
    {
    }

    internal CodexInteractiveSessionLauncher(Action<ProcessStartInfo> startProcess)
        : this(startInfo =>
        {
            startProcess(startInfo);
            return null;
        }, null)
    {
    }

    internal CodexInteractiveSessionLauncher(
        Func<ProcessStartInfo, Process?> startProcess,
        Func<Workbench.Runtime.Providers.ProviderAccountId, CancellationToken, Task<bool>>? releaseRuntime)
    {
        _startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
        _releaseRuntime = releaseRuntime;
    }

    public bool CanOpen(WorkerSessionRecord worker) =>
        string.Equals(worker.Session.ProviderId.Value, "codex", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(worker.Session.ExternalSessionId) &&
        !string.IsNullOrWhiteSpace(worker.Session.WorkingDirectory);

    public async Task<InteractiveSessionOpenResult> OpenAsync(
        WorkerSessionRecord worker,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // An external CLI is a separate runtime writer. Never release the
        // Workbench runtime while this hosted session is active; the caller
        // should use the hosted Surface instead.
        if (worker.Session.Status is AgentSessionStatus.Running or AgentSessionStatus.WaitingApproval)
        {
            return InteractiveSessionOpenResult.Failure(
                "This Worker is active in Workbench. Open its hosted Surface instead of taking over the runtime.");
        }

        if (!string.Equals(worker.Session.ProviderId.Value, "codex", StringComparison.Ordinal))
        {
            return InteractiveSessionOpenResult.Failure(
                "This Worker runtime does not support native Codex sessions.");
        }

        if (string.IsNullOrWhiteSpace(worker.Session.ExternalSessionId) ||
            string.IsNullOrWhiteSpace(worker.Session.WorkingDirectory))
        {
            return InteractiveSessionOpenResult.Failure(
                "This Worker has no resumable Codex session.");
        }

        try
        {
            if (_releaseRuntime is not null &&
                !await _releaseRuntime(worker.Session.AccountId, cancellationToken).ConfigureAwait(false))
            {
                return InteractiveSessionOpenResult.Failure("Workbench could not release the Worker session for CLI takeover.");
            }

            var process = _startProcess(CreateStartInfo(worker));
            _ = CenterWindowAsync($"[Worker] {worker.TaskTitle} - Codex");
            return InteractiveSessionOpenResult.Success(process?.WaitForExitAsync(), process?.Id);
        }
        catch (Exception)
        {
            return InteractiveSessionOpenResult.Failure("Could not open the native Codex window.");
        }
    }

    internal static ProcessStartInfo CreateStartInfo(WorkerSessionRecord worker)
    {
        var startInfo = new ProcessStartInfo { FileName = "wt.exe", UseShellExecute = true };
        var title = $"[Worker] {worker.TaskTitle} - Codex";
        startInfo.ArgumentList.Add("--wait");
        startInfo.ArgumentList.Add("-w");
        startInfo.ArgumentList.Add("new");
        startInfo.ArgumentList.Add("--size");
        startInfo.ArgumentList.Add("120,42");
        startInfo.ArgumentList.Add("--pos");
        startInfo.ArgumentList.Add("24,24");
        startInfo.ArgumentList.Add("--title");
        startInfo.ArgumentList.Add(title);
        startInfo.ArgumentList.Add("--suppressApplicationTitle");
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(worker.Session.WorkingDirectory!);
        startInfo.ArgumentList.Add("cmd.exe");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("codex");
        startInfo.ArgumentList.Add("resume");
        startInfo.ArgumentList.Add(worker.Session.ExternalSessionId!);
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(worker.Session.WorkingDirectory!);
        return startInfo;
    }


    private static async Task CenterWindowAsync(string title)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var handle = FindVisibleWindow(title);
            if (handle != IntPtr.Zero)
            {
                CenterWindow(handle);
                return;
            }

            await Task.Delay(150).ConfigureAwait(false);
        }
    }

    private static IntPtr FindVisibleWindow(string title)
    {
        var result = IntPtr.Zero;
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle)) return true;
            var text = new StringBuilder(512);
            _ = GetWindowText(handle, text, text.Capacity);
            if (string.Equals(text.ToString(), title, StringComparison.Ordinal))
            {
                result = handle;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static void CenterWindow(IntPtr handle)
    {
        if (!GetWindowRect(handle, out var window)) return;
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;

        var width = window.Right - window.Left;
        var height = window.Bottom - window.Top;
        const int margin = 24;
        var areaWidth = info.Work.Right - info.Work.Left;
        var areaHeight = info.Work.Bottom - info.Work.Top;
        var x = info.Work.Left + Math.Max(margin, (areaWidth - width) / 2);
        var y = info.Work.Top + Math.Max(margin, (areaHeight - height) / 2);
        x = Math.Min(x, info.Work.Right - width - margin);
        y = Math.Min(y, info.Work.Bottom - height - margin);
        _ = SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0,
            SetWindowPosNoSize | SetWindowPosNoZOrder | SetWindowPosNoActivate);
    }

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maxLength);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    private const uint MonitorDefaultToNearest = 2;
    private const uint SetWindowPosNoSize = 0x0001;
    private const uint SetWindowPosNoZOrder = 0x0004;
    private const uint SetWindowPosNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }
}
