using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.Worker;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.App.Services;
using Workbench.Storage.Tasks;
using Workbench.Runtime.Runtime;
using Workbench.App.AgentHost;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.ViewModels.Panes;

public sealed partial class WorkPaneViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly Func<Task> _focus;
    private readonly IWorkerRoutingStore? _store;
    private readonly AgentRuntimeRegistry? _runtimes;
    private readonly IAgentInteractiveSessionLauncher? _interactiveLauncher;
    private readonly WorkerRemovalService? _removalService;
    private readonly TaskRevisionRepository? _taskRevisions;
    private readonly WorkerSessionRouter? _workerRouter;
    private readonly CoreProject? _project;
    private readonly Func<CancellationToken, Task>? _restoreRuntimeAfterExternalCli;
    private readonly Func<Workbench.Runtime.Providers.ProviderAccountId, CancellationToken, Task<bool>>? _releaseRuntimeForExternalCli;
    private readonly IAgentHost? _agentHost;
    private readonly Func<WorkerSessionCardViewModel, Task>? _openHostedSurface;
    private CancellationTokenSource? _monitorCancellation;
    private WorkerSessionCardViewModel? _externalCliWorker;
    public WorkPaneViewModel(Func<Task> focus, IWorkerRoutingStore? store = null, AgentRuntimeRegistry? runtimes = null, IAgentInteractiveSessionLauncher? interactiveLauncher = null, WorkerRemovalService? removalService = null, TaskRevisionRepository? taskRevisions = null, WorkerSessionRouter? workerRouter = null, CoreProject? project = null, Func<CancellationToken, Task>? restoreRuntimeAfterExternalCli = null, Func<Workbench.Runtime.Providers.ProviderAccountId, CancellationToken, Task<bool>>? releaseRuntimeForExternalCli = null, IAgentHost? agentHost = null, Func<WorkerSessionCardViewModel, Task>? openHostedSurface = null) { _focus = focus; _store = store; _runtimes = runtimes; _interactiveLauncher = interactiveLauncher; _taskRevisions = taskRevisions; _workerRouter = workerRouter; _project = project; _restoreRuntimeAfterExternalCli = restoreRuntimeAfterExternalCli; _releaseRuntimeForExternalCli = releaseRuntimeForExternalCli; _agentHost = agentHost; _openHostedSurface = openHostedSurface; _removalService = removalService ?? (store is not null && runtimes is not null ? new WorkerRemovalService(runtimes, store, TimeProvider.System) : null); if (_agentHost is not null) _agentHost.EventReceived += OnAgentHostEvent; }
    public ObservableCollection<WorkerSessionCardViewModel> Workers { get; } = [];
    public ObservableCollection<WorkerTranscriptLineViewModel> Transcript { get; } = [];
    public bool HasWorkers => Workers.Count > 0;
    [ObservableProperty] public partial WorkerSessionCardViewModel? SelectedWorker { get; set; }
    [ObservableProperty] public partial string? OpenError { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPendingWorkerRemoval))] public partial WorkerSessionCardViewModel? PendingWorkerRemoval { get; set; }
    [ObservableProperty] public partial string? RemovalError { get; set; }
    [ObservableProperty] public partial string? StatusError { get; set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasWorkerDetails))] public partial WorkerSessionCardViewModel? DetailedWorker { get; set; }
    public bool HasPendingWorkerRemoval => PendingWorkerRemoval is not null;
    public bool HasWorkerDetails => DetailedWorker is not null;
    public bool CanCollapseAllWorkers => Workers.Any(item => !item.IsCompact);
    public async Task LoadAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        Workers.Clear(); if (_store is null) return;
        var sessions = await _store.ListSessionsAsync(projectId, cancellationToken);
        foreach (var session in sessions.OrderBy(item => Rank(item.Session.Status)).ThenByDescending(item => item.LastActiveAt))
        {
            var item = await ReconcileStatusAsync(session, cancellationToken);
            item = await ReconcileStatusAsync(item, cancellationToken);
            var plan = await LoadPlanAsync(item, cancellationToken);
            var card = new WorkerSessionCardViewModel(item, plan,
                compact: item.Session.Status == AgentSessionStatus.Completed && sessions.Count > 3);
            ApplyHostedProgress(card);
            var lease = await _store.GetActiveSurfaceLeaseAsync(item.ProjectId, item.TaskId, item.Session.Id, cancellationToken);
            if (lease is not null)
            {
                if (IsProcessAlive(lease.ProcessId))
                {
                    var runtimeReleased = true;
                    if (_releaseRuntimeForExternalCli is not null)
                    {
                        try
                        {
                            runtimeReleased = await _releaseRuntimeForExternalCli(item.Session.AccountId, cancellationToken);
                        }
                        catch
                        {
                            runtimeReleased = false;
                        }
                    }

                    if (runtimeReleased)
                    {
                        card.SetExternalCliActive(true, lease);
                        _externalCliWorker ??= card;
                    }
                    else
                    {
                        // Do not claim that the CLI owns the session while the
                        // Workbench runtime may still be attached. This avoids
                        // a silent dual-writer condition after restart.
                        var recoveryLease = lease with
                        {
                            // Keep the lease active while the external process
                            // is alive. It is unsafe to silently hand the
                            // session back to Workbench when release failed.
                            Active = true,
                            ProcessId = lease.ProcessId,
                            ChangedAt = DateTimeOffset.UtcNow,
                            State = WorkerSessionSurfaceState.RecoveryRequired
                        };
                        await _store.AppendSurfaceLeaseAsync(recoveryLease, cancellationToken);
                        card.SetExternalCliActive(true, recoveryLease);
                        OpenError ??= "Workbench 无法安全释放当前 Agent 连接，CLI 接管已暂停。请先停止其他运行中的任务后重试。";
                    }
                }
                else
                {
                    await _store.AppendSurfaceLeaseAsync(lease with
                    {
                        Active = false,
                        ProcessId = null,
                        ChangedAt = DateTimeOffset.UtcNow,
                        State = WorkerSessionSurfaceState.WorkbenchOwned
                    }, cancellationToken);
                }
            }
            Workers.Add(card);
        }
        OnPropertyChanged(nameof(HasWorkers));
        OnPropertyChanged(nameof(CanCollapseAllWorkers));
    }

    [RelayCommand]
    private void CollapseAllWorkers()
    {
        foreach (var worker in Workers)
            worker.IsCompact = true;
        OnPropertyChanged(nameof(CanCollapseAllWorkers));
    }

    private void ApplyHostedProgress(WorkerSessionCardViewModel worker)
    {
        if (_agentHost is null || !worker.HasPlan) return;
        try
        {
            var snapshot = _agentHost.Attach(worker.Session);
            if (snapshot.Session.Status != worker.Session.Status && snapshot.Session.UpdatedAt >= worker.Session.UpdatedAt)
                worker.UpdateSession(snapshot.Session);
            var completedActivities = snapshot.Events.OfType<AgentToolEvent>().Count(item => item.IsCompleted);
            var completedSteps = snapshot.Events.OfType<AgentProgressChanged>().Select(item => item.CompletedSteps).DefaultIfEmpty(0).Max();
            var latestPlan = snapshot.Events.OfType<AgentPlanUpdated>().LastOrDefault();
            if (latestPlan is not null)
                worker.ApplyPlanUpdate(latestPlan.Steps);
            worker.ApplyProgress(Math.Max(completedActivities, completedSteps), snapshot.Session.Status == AgentSessionStatus.Completed);
        }
        catch
        {
            // Progress is advisory and must not prevent the Worker list from loading.
        }
    }

    private void OnAgentHostEvent(object? sender, HostedAgentEvent item)
    {
        try
        {
            DispatchToUi(() =>
            {
                try
                {
                    var worker = Workers.FirstOrDefault(candidate => candidate.Session.Id == item.SessionId);
                    if (worker is null) return;
                    switch (item.Event)
                    {
                        case AgentProgressChanged progress:
                            worker.ApplyProgress(progress.CompletedSteps);
                            break;
                        case AgentPlanUpdated plan:
                            worker.ApplyPlanUpdate(plan.Steps);
                            break;
                        case AgentTurnCompleted completed:
                            worker.UpdateSession(worker.Session with { Status = completed.Result.FinalStatus, UpdatedAt = completed.OccurredAt });
                            worker.ApplyProgress(worker.PlanSteps.Count, terminalCompleted: completed.Result.FinalStatus == AgentSessionStatus.Completed);
                            OnPropertyChanged(nameof(SelectedWorker));
                            break;
                        case AgentStatusChanged status:
                            worker.UpdateSession(worker.Session with { Status = status.Status, UpdatedAt = status.OccurredAt });
                            if (status.Status == AgentSessionStatus.Completed)
                                worker.ApplyProgress(worker.PlanSteps.Count, terminalCompleted: true);
                            OnPropertyChanged(nameof(SelectedWorker));
                            break;
                    }
                }
                catch { }
            });
        }
        catch { }
    }

    private static void DispatchToUi(Action action)
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) action();
        else Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    private async Task<WorkerSessionRecord> ReconcileStatusAsync(WorkerSessionRecord worker, CancellationToken cancellationToken)
    {
        if (_store is null || (_runtimes is null && _agentHost is null) || worker.Session.Status is not (AgentSessionStatus.Running or AgentSessionStatus.WaitingApproval)) return worker;
        if (worker.LastActiveAt > DateTimeOffset.UtcNow.AddSeconds(-2)) return worker;
        IAgentRuntime runtime;
        try { runtime = _runtimes?.GetByAccount(worker.Session.AccountId) ?? throw new KeyNotFoundException(); }
        catch (KeyNotFoundException) { return worker; }
        AgentSessionStatus observed;
        try { observed = _agentHost is not null ? await _agentHost.GetStatusAsync(worker.Session, cancellationToken) : await runtime.GetStatusAsync(worker.Session, cancellationToken); }
        catch { return worker; }
        if (observed is AgentSessionStatus.Ready or AgentSessionStatus.Created) observed = AgentSessionStatus.Interrupted;
        if (observed is not (AgentSessionStatus.Completed or AgentSessionStatus.Failed or AgentSessionStatus.Interrupted) || observed == worker.Session.Status) return worker;
        try
        {
            var changedAt = DateTimeOffset.UtcNow;
            await _store.OverrideStatusAsync(new WorkerStatusOverride(worker.ProjectId, worker.TaskId, worker.Session.Id, observed, changedAt, "Provider status reconciled by Workbench"), cancellationToken);
            return worker with { Session = worker.Session with { Status = observed, UpdatedAt = changedAt }, LastActiveAt = changedAt };
        }
        catch { return worker; }
    }

    private async Task<IReadOnlyList<string>> LoadPlanAsync(WorkerSessionRecord worker, CancellationToken cancellationToken)
    {
        if (_taskRevisions is null || worker.TaskRevisionId == Guid.Empty) return [];
        var revisions = await _taskRevisions.ListAsync(worker.ProjectId, worker.TaskId, cancellationToken);
        return revisions.FirstOrDefault(item => item.Id == worker.TaskRevisionId)?.Acceptance ?? [];
    }
    public async Task SelectWorkerAsync(WorkerSessionCardViewModel worker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(worker);
        _monitorCancellation?.Cancel();
        _monitorCancellation?.Dispose();
        _monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        SelectedWorker = worker;
        await RefreshTranscriptAsync(worker, _monitorCancellation.Token);
        ApplyHostedProgress(worker);
        if (worker.Session.Status is AgentSessionStatus.Running or AgentSessionStatus.WaitingApproval)
        {
            _ = MonitorWorkerAsync(worker, _monitorCancellation.Token);
        }
    }

    private async Task RefreshTranscriptAsync(WorkerSessionCardViewModel worker, CancellationToken cancellationToken)
    {
        if (_runtimes is null && _agentHost is null) return;
        try
        {
            var events = _agentHost is not null
                ? await _agentHost.GetTranscriptAsync(worker.Session, cancellationToken)
                : await _runtimes!.GetByAccount(worker.Session.AccountId).GetTranscriptAsync(worker.Session, cancellationToken);
            Transcript.Clear();
            foreach (var item in events.OfType<AgentMessage>())
            {
                var text = item.Role == AgentMessageRole.User
                    ? DisplayTranscriptText(item.Text)
                    : AgentProgressProtocol.StripControlLines(item.Text);
                if (!string.IsNullOrWhiteSpace(text))
                    Transcript.Add(new WorkerTranscriptLineViewModel(item.Role.ToString(), text));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // A transient transcript read failure must not stop the Worker.
        }
    }

    private static string DisplayTranscriptText(string text)
    {
        var marker = text.IndexOf("\n---\nname: workbench-worker", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            marker = text.IndexOf("\n# Workbench Worker", StringComparison.Ordinal);
        return AgentProgressProtocol.StripControlLines(marker >= 0 ? text[..marker].TrimEnd() : text);
    }

    private async Task MonitorWorkerAsync(WorkerSessionCardViewModel worker, CancellationToken cancellationToken)
    {
        if (_runtimes is null && _agentHost is null) return;
        IAgentRuntime runtime;
        try { runtime = _runtimes?.GetByAccount(worker.Session.AccountId) ?? throw new KeyNotFoundException(); }
        catch (KeyNotFoundException) { return; }

        while (!cancellationToken.IsCancellationRequested && ReferenceEquals(SelectedWorker, worker))
        {
            try
            {
                var persisted = _store is null
                    ? null
                    : await _store.GetSessionAsync(worker.Record.ProjectId, worker.Record.TaskId, worker.Session.Id, cancellationToken);
                var observed = persisted?.Session.Status is AgentSessionStatus.Completed or AgentSessionStatus.Failed or AgentSessionStatus.Interrupted or AgentSessionStatus.Stopped or AgentSessionStatus.Archived
                    ? persisted.Session.Status
                    : _agentHost is not null
                        ? await _agentHost.GetStatusAsync(worker.Session, cancellationToken)
                        : await runtime.GetStatusAsync(worker.Session, cancellationToken);
                if (observed is AgentSessionStatus.Ready or AgentSessionStatus.Created)
                    observed = AgentSessionStatus.Interrupted;

                if (persisted is not null && persisted.Session.Status != worker.Session.Status)
                {
                    worker.UpdateSession(persisted.Session);
                    OnPropertyChanged(nameof(SelectedWorker));
                }
                else if (observed != worker.Session.Status)
                {
                    worker.UpdateSession(worker.Session with
                    {
                        Status = observed,
                        UpdatedAt = DateTimeOffset.UtcNow
                    });
                    OnPropertyChanged(nameof(SelectedWorker));
                }

                await RefreshTranscriptAsync(worker, cancellationToken);
                ApplyHostedProgress(worker);
                if (observed is not (AgentSessionStatus.Running or AgentSessionStatus.WaitingApproval)) return;
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                try { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
    public async Task OpenWorkerAsync(WorkerSessionCardViewModel worker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(worker);
        OpenError = null;
        var record = await ReconcileStatusAsync(worker.Record, cancellationToken);
        if (_openHostedSurface is not null && _agentHost is not null)
        {
            await _openHostedSurface(worker);
            return;
        }
        if (ReferenceEquals(_externalCliWorker, worker) || worker.IsExternalCliActive)
        {
            OpenError = "此 Worker 已由 CLI 接管。关闭 CLI 后，Workbench 会自动恢复监管。";
            return;
        }
        if (record.Session.Status is AgentSessionStatus.Running or AgentSessionStatus.WaitingApproval)
        {
            OpenError = LocalizationService.Current["Dynamic.WorkerBusyInWorkbench"];
            return;
        }

        if (_interactiveLauncher is null || !_interactiveLauncher.CanOpen(record))
        {
            OpenError = LocalizationService.Current["Dynamic.WorkerRuntimeUnsupported"];
            return;
        }

        var continuationAttemptId = Guid.NewGuid();
        if (_store is not null)
        {
            await _store.AppendSurfaceLeaseAsync(new WorkerSessionSurfaceLease(
                record.ProjectId,
                record.TaskId,
                record.Session.Id,
                continuationAttemptId,
                null,
                true,
                DateTimeOffset.UtcNow,
                WorkerSessionSurfaceState.Preparing), CancellationToken.None);
        }

        var result = await _interactiveLauncher.OpenAsync(record, cancellationToken);
        OpenError = result.Error;
        if (result.Succeeded && result.Completed is not null)
        {
            if (_store is not null)
            {
                await _store.AppendSurfaceLeaseAsync(new WorkerSessionSurfaceLease(
                    record.ProjectId, record.TaskId, record.Session.Id, continuationAttemptId,
                    result.ProcessId, true, DateTimeOffset.UtcNow, WorkerSessionSurfaceState.CliOwned), CancellationToken.None);
            }
            _externalCliWorker = worker;
            worker.SetExternalCliActive(true, new WorkerSessionSurfaceLease(
                record.ProjectId, record.TaskId, record.Session.Id, continuationAttemptId,
                result.ProcessId, true, DateTimeOffset.UtcNow, WorkerSessionSurfaceState.CliOwned));
            _ = ReacquireAfterExternalCliAsync(worker, result.Completed, continuationAttemptId);
        }
        else if (result.Succeeded)
        {
            // A launcher that cannot expose a process lifetime cannot safely
            // participate in ownership handoff; close the preparing lease.
            if (_store is not null)
            {
                await _store.AppendSurfaceLeaseAsync(new WorkerSessionSurfaceLease(
                    record.ProjectId, record.TaskId, record.Session.Id, continuationAttemptId, null, false,
                    DateTimeOffset.UtcNow, WorkerSessionSurfaceState.WorkbenchOwned), CancellationToken.None);
            }
        }
        else if (!result.Succeeded)
        {
            if (_store is not null)
            {
                await _store.AppendSurfaceLeaseAsync(new WorkerSessionSurfaceLease(
                    record.ProjectId, record.TaskId, record.Session.Id, continuationAttemptId, null, false,
                    DateTimeOffset.UtcNow, WorkerSessionSurfaceState.WorkbenchOwned), CancellationToken.None);
            }
            await RestoreRuntimeAfterExternalCliAsync();
        }
    }

    private async Task ReacquireAfterExternalCliAsync(WorkerSessionCardViewModel worker, Task cliCompletion, Guid continuationAttemptId)
    {
        try { await cliCompletion.ConfigureAwait(false); }
        catch { }

        if (_store is not null)
        {
            await _store.AppendSurfaceLeaseAsync(new WorkerSessionSurfaceLease(
                worker.Record.ProjectId, worker.Record.TaskId, worker.Session.Id, continuationAttemptId,
                null, true, DateTimeOffset.UtcNow, WorkerSessionSurfaceState.Reacquiring), CancellationToken.None).ConfigureAwait(false);
        }
        var reacquired = await RestoreRuntimeAfterExternalCliAsync().ConfigureAwait(false);
        if (_store is not null)
        {
            await _store.AppendSurfaceLeaseAsync(new WorkerSessionSurfaceLease(
                worker.Record.ProjectId,
                worker.Record.TaskId,
                worker.Session.Id,
                continuationAttemptId,
                null,
                false,
                DateTimeOffset.UtcNow, reacquired ? WorkerSessionSurfaceState.WorkbenchOwned : WorkerSessionSurfaceState.RecoveryRequired), CancellationToken.None).ConfigureAwait(false);
        }
        if (ReferenceEquals(_externalCliWorker, worker)) _externalCliWorker = null;
        worker.SetExternalCliActive(!reacquired, new WorkerSessionSurfaceLease(
            worker.Record.ProjectId, worker.Record.TaskId, worker.Session.Id, continuationAttemptId,
            null, !reacquired, DateTimeOffset.UtcNow,
            reacquired ? WorkerSessionSurfaceState.WorkbenchOwned : WorkerSessionSurfaceState.RecoveryRequired));
        try
        {
            await LoadAsync(worker.Record.ProjectId).ConfigureAwait(false);
            if (ReferenceEquals(SelectedWorker, worker))
            {
                var refreshed = Workers.FirstOrDefault(item => item.Session.Id == worker.Session.Id);
                if (refreshed is not null)
                {
                    await SelectWorkerAsync(refreshed).ConfigureAwait(false);
                }
            }
        }
        catch { }
    }

    private async Task<bool> RestoreRuntimeAfterExternalCliAsync()
    {
        if (_restoreRuntimeAfterExternalCli is null) return true;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                await _restoreRuntimeAfterExternalCli(CancellationToken.None).ConfigureAwait(false);
                return true;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1))).ConfigureAwait(false);
            }
        }
        return false;
    }

    private static bool IsProcessAlive(int? processId)
    {
        if (processId is not > 0) return false;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId.Value);
            return !process.HasExited;
        }
        catch { return false; }
    }

    public Task ActivateWorkerCardAsync(WorkerSessionCardViewModel worker, CancellationToken cancellationToken = default) =>
        OpenWorkerAsync(worker, cancellationToken);

    /// <summary>
    /// Sends an explicit user direction to a hosted Worker session. While a
    /// turn is active this becomes a steer; otherwise it starts a new
    /// continuation through the normal Worker router so its result still
    /// follows the Handoff path.
    /// </summary>
    public async Task SendWorkerMessageAsync(
        WorkerSessionCardViewModel worker,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (_agentHost is null)
        {
            throw new InvalidOperationException("The Worker Agent Host is not available.");
        }

        if (worker.IsExternalCliActive)
        {
            throw new InvalidOperationException("This Worker is currently attached to an external CLI surface.");
        }

        if (_workerRouter is null || _project is null || _store is null)
        {
            throw new InvalidOperationException("The Worker continuation router is not available.");
        }
        var result = await _workerRouter.SendIntentAsync(
            _project,
            worker.Record.TaskId,
            worker.Session.Id,
            text,
            AgentIntentSource.User,
            cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Error ?? "The Worker could not accept the user direction.");
        }

        await LoadAsync(worker.Record.ProjectId, cancellationToken);
    }

    public Task StopWorkerAsync(
        WorkerSessionCardViewModel worker,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(worker);
        if (_agentHost is null)
        {
            throw new InvalidOperationException("The Worker Agent Host is not available.");
        }

        return _agentHost.StopAsync(worker.Session, cancellationToken);
    }

    [RelayCommand] private Task OpenWorker(WorkerSessionCardViewModel worker) => OpenWorkerAsync(worker);
    [RelayCommand] private async Task ContinueWorker(WorkerSessionCardViewModel worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        if (!worker.CanContinue) return;
        OpenError = null;
        if (_workerRouter is null || _project is null || _store is null)
        {
            await OpenWorkerAsync(worker);
            return;
        }

        try
        {
            var revisionId = worker.Record.TaskRevisionId;
            var revisions = _taskRevisions is null
                ? Array.Empty<Workbench.Core.Tasks.TaskRevision>()
                : await _taskRevisions.ListAsync(worker.Record.ProjectId, worker.Record.TaskId);
            var revision = revisions.FirstOrDefault(item => item.Id == revisionId) ?? revisions.LastOrDefault();
            revisionId = revision?.Id ?? revisionId;
            var acceptance = revision is null ? string.Empty : string.Join("\n", revision.Acceptance.Select(item => $"- {item}"));
            var prompt = $"上一轮 Worker 执行因 Agent 对话中断或操作失败而停止。请复用当前会话，检查工作区现状，继续完成原 Assignment。\n\n目标：{revision?.Goal ?? worker.TaskTitle}\n范围：{revision?.Scope ?? "按原 Assignment 继续"}\n验收标准：\n{acceptance}";
            var runningRecord = worker.Record with
            {
                Session = worker.Session with { Status = AgentSessionStatus.Running, UpdatedAt = DateTimeOffset.UtcNow },
                LastActiveAt = DateTimeOffset.UtcNow
            };
            await _store.SaveSessionAsync(runningRecord);
            var result = await _workerRouter.StartAsync(new WorkerStartRequest(
                _project, worker.Record.TaskId, revisionId, worker.TaskTitle, worker.Record.Profile, prompt,
                worker.Session.Id, worker.WorkerLabel, WaitForCompletion: false));
            if (!result.Succeeded) OpenError = result.Error;
            await LoadAsync(worker.Record.ProjectId);
        }
        catch (Exception exception)
        {
            OpenError = exception.Message;
        }
    }
    [RelayCommand] private void RequestWorkerRemoval(WorkerSessionCardViewModel worker)
    {
        PendingWorkerRemoval = worker;
        RemovalError = null;
    }
    [RelayCommand] private void RequestWorkerDetails(WorkerSessionCardViewModel worker) => DetailedWorker = worker;
    [RelayCommand] private async Task RefreshWorkerStatus(WorkerSessionCardViewModel worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        StatusError = null;
        try
        {
            await LoadAsync(worker.Record.ProjectId);
        }
        catch (Exception exception)
        {
            StatusError = exception.Message;
        }
    }
    [RelayCommand] private async Task MarkWorkerCompleted(WorkerSessionCardViewModel worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        if (_store is null) return;
        StatusError = null;
        try
        {
            await _store.OverrideStatusAsync(new WorkerStatusOverride(
                worker.Record.ProjectId,
                worker.Record.TaskId,
                worker.Record.Session.Id,
                AgentSessionStatus.Completed,
                DateTimeOffset.UtcNow,
                "User marked Worker completed in Workbench"));
            await LoadAsync(worker.Record.ProjectId);
        }
        catch (Exception exception)
        {
            StatusError = exception.Message;
        }
    }
    [RelayCommand] private void CloseWorkerDetails() => DetailedWorker = null;
    [RelayCommand] private void CancelWorkerRemoval()
    {
        PendingWorkerRemoval = null;
        RemovalError = null;
    }
    [RelayCommand] private async Task ConfirmWorkerRemoval()
    {
        if (PendingWorkerRemoval is not { } worker || _removalService is null)
        {
            return;
        }

        RemovalError = null;
        var result = await _removalService.RemoveAsync(worker.Record);
        if (!result.Succeeded)
        {
            RemovalError = result.Error;
            return;
        }

        Workers.Remove(worker);
        if (SelectedWorker == worker) SelectedWorker = null;
        if (DetailedWorker == worker) DetailedWorker = null;
        PendingWorkerRemoval = null;
        OnPropertyChanged(nameof(HasWorkers));
    }
    [RelayCommand] private Task Focus() => _focus();
    public ValueTask DisposeAsync()
    {
        _monitorCancellation?.Cancel();
        _monitorCancellation?.Dispose();
        _monitorCancellation = null;
        if (_agentHost is not null)
            _agentHost.EventReceived -= OnAgentHostEvent;
        return ValueTask.CompletedTask;
    }
    private static int Rank(AgentSessionStatus status) => status switch { AgentSessionStatus.Running => 0, AgentSessionStatus.WaitingApproval => 1, AgentSessionStatus.Ready => 2, AgentSessionStatus.Interrupted or AgentSessionStatus.Failed => 3, AgentSessionStatus.Completed => 4, _ => 5 };
}
public sealed partial class WorkerSessionCardViewModel : ObservableObject
{
    private WorkerSessionRecord record;
    private int _completedStepCount;
    private bool _hasNativePlan;
    private Guid? _continuationAttemptId;
    private int? _externalCliProcessId;
    private WorkerSessionSurfaceState _surfaceState = WorkerSessionSurfaceState.WorkbenchOwned;
    public WorkerSessionCardViewModel(WorkerSessionRecord record, IReadOnlyList<string>? plan = null, bool compact = false)
    {
        this.record = record;
        PlanSteps = BuildPlan(plan ?? []);
        IsCompact = compact;
    }

    internal WorkerSessionRecord Record => record;
    public string TaskTitle => record.TaskTitle; public string WorkerLabel => record.Label; public string Profile => record.Profile.ModelProfileId; public AgentSession Session => record.Session;
    public string SessionId => record.Session.Id.Value.ToString();
    public string ExternalSessionId => record.Session.ExternalSessionId ?? "未提供";
    public string WorkingDirectory => record.Session.WorkingDirectory ?? "未提供";
    public string Runtime => record.Profile.AgentRuntimeId;
    public string Status => record.Session.Status switch { AgentSessionStatus.Running => LocalizationService.Current["Dynamic.Working"], AgentSessionStatus.WaitingApproval => LocalizationService.Current["Dynamic.Waiting"], AgentSessionStatus.Ready => LocalizationService.Current["Dynamic.Ready"], AgentSessionStatus.Completed => LocalizationService.Current["Dynamic.Completed"], AgentSessionStatus.Interrupted => LocalizationService.Current["Dynamic.Interrupted"], AgentSessionStatus.Failed => LocalizationService.Current["Dynamic.Failed"], AgentSessionStatus.Stopped or AgentSessionStatus.Archived => LocalizationService.Current["Dynamic.Closed"], _ => record.Session.Status.ToString() };
    public bool CanContinue => record.Session.Status is AgentSessionStatus.Interrupted or AgentSessionStatus.Failed;
    public string LastActiveAtText => record.LastActiveAt.LocalDateTime.ToString("g");
    public string CompactTimeText => record.LastActiveAt.LocalDateTime.ToString("MM/dd HH:mm");
    [ObservableProperty] public partial bool IsCompact { get; set; }
    [RelayCommand] private void ToggleCompact() => IsCompact = !IsCompact;
    public IReadOnlyList<WorkerPlanStepViewModel> PlanSteps { get; }
    public bool HasPlan => PlanSteps.Count > 0;
    public string ProgressText => !HasPlan ? string.Empty : record.Session.Status == AgentSessionStatus.Completed ? $"{PlanSteps.Count}/{PlanSteps.Count} · 已完成" : $"{PlanSteps.Count(step => step.Marker == "✓")}/{PlanSteps.Count}";
    public string CurrentPlanStep => PlanSteps.FirstOrDefault(step => step.IsCurrent)?.Text ?? string.Empty;
    [ObservableProperty] public partial bool IsExternalCliActive { get; set; }
    public string SurfaceStatus => _surfaceState switch
    {
        WorkerSessionSurfaceState.Preparing => "准备 CLI 接管",
        WorkerSessionSurfaceState.CliOwned => "CLI 已接管",
        WorkerSessionSurfaceState.Reacquiring => "正在恢复监管",
        WorkerSessionSurfaceState.RecoveryRequired => "需要恢复监管",
        _ => "Workbench"
    };
    public string ContinuationAttemptId => _continuationAttemptId?.ToString() ?? "未创建";
    public string ExternalCliProcessId => _externalCliProcessId?.ToString() ?? "未记录";

    internal void SetExternalCliActive(bool active, WorkerSessionSurfaceLease? lease = null)
    {
        _continuationAttemptId = active ? lease?.ContinuationAttemptId : null;
        _externalCliProcessId = active ? lease?.ProcessId : null;
        _surfaceState = active ? lease?.State ?? WorkerSessionSurfaceState.CliOwned : WorkerSessionSurfaceState.WorkbenchOwned;
        IsExternalCliActive = active;
        OnPropertyChanged(nameof(SurfaceStatus));
        OnPropertyChanged(nameof(ContinuationAttemptId));
        OnPropertyChanged(nameof(ExternalCliProcessId));
    }

    internal void UpdateSession(AgentSession session)
    {
        record = record with { Session = session, LastActiveAt = session.UpdatedAt };
        OnPropertyChanged(nameof(Session));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(LastActiveAtText));
        OnPropertyChanged(nameof(CompactTimeText));
        OnPropertyChanged(nameof(ProgressText));
    }

    internal void ApplyProgress(int completedActivities, bool terminalCompleted = false)
    {
        if (!HasPlan) return;
        if (_hasNativePlan && !terminalCompleted) return;
        _completedStepCount = terminalCompleted
            ? PlanSteps.Count
            : Math.Max(_completedStepCount, Math.Clamp(completedActivities, 0, PlanSteps.Count));
        var completed = _completedStepCount;
        var current = terminalCompleted || completed >= PlanSteps.Count ? -1 : completed;
        for (var index = 0; index < PlanSteps.Count; index++)
        {
            var marker = index < completed
                ? "✓"
                : index == current && (record.Session.Status is AgentSessionStatus.Failed or AgentSessionStatus.Interrupted)
                    ? "!"
                    : index == current ? "●" : "○";
            PlanSteps[index].SetState(marker, index == current);
        }
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(CurrentPlanStep));
    }

    internal void ApplyPlanUpdate(IReadOnlyList<AgentPlanStep> steps)
    {
        if (!HasPlan || steps.Count == 0) return;
        _hasNativePlan = true;
        _completedStepCount = Math.Max(_completedStepCount,
            steps.Count(item => item.Status == AgentPlanStepStatus.Completed));
        for (var index = 0; index < PlanSteps.Count; index++)
        {
            var status = index < steps.Count ? steps[index].Status : AgentPlanStepStatus.Pending;
            var marker = status switch
            {
                AgentPlanStepStatus.Completed => "✓",
                AgentPlanStepStatus.InProgress => "●",
                AgentPlanStepStatus.Failed => "!",
                _ => "○"
            };
            PlanSteps[index].SetState(marker, status == AgentPlanStepStatus.InProgress);
        }
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(CurrentPlanStep));
    }

    private IReadOnlyList<WorkerPlanStepViewModel> BuildPlan(IReadOnlyList<string> plan)
    {
        if (plan.Count == 0) return [];
        var completed = record.Session.Status == AgentSessionStatus.Completed ? plan.Count : 0;
        var current = record.Session.Status is AgentSessionStatus.Running or AgentSessionStatus.WaitingApproval ? 0 : -1;
        return plan.Select((text, index) => new WorkerPlanStepViewModel(
            index < completed ? "✓" : index == current ? "●" : record.Session.Status is AgentSessionStatus.Failed or AgentSessionStatus.Interrupted && index == current ? "!" : "○",
            text,
            index == current)).ToArray();
    }
}
public sealed class WorkerPlanStepViewModel : ObservableObject
{
    public WorkerPlanStepViewModel(string marker, string text, bool isCurrent)
    {
        Marker = marker;
        Text = text;
        IsCurrent = isCurrent;
        Opacity = isCurrent ? 1d : 0.86d;
    }

    public string Marker { get; private set; }
    public string Text { get; }
    public bool IsCurrent { get; private set; }

    private double _opacity;
    public double Opacity
    {
        get => _opacity;
        private set => SetProperty(ref _opacity, value);
    }

    internal void SetPulse(double opacity) => Opacity = IsCurrent ? opacity : 0.86d;

    internal void SetState(string marker, bool isCurrent)
    {
        Marker = marker;
        IsCurrent = isCurrent;
        Opacity = isCurrent ? 1d : 0.86d;
        OnPropertyChanged(nameof(Marker));
        OnPropertyChanged(nameof(IsCurrent));
    }
}
public sealed record WorkerTranscriptLineViewModel(string Role, string Text);
