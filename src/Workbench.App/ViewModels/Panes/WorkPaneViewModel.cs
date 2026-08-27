using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.Worker;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.App.Services;
using Workbench.Storage.Tasks;
using Workbench.Runtime.Runtime;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.ViewModels.Panes;

public sealed partial class WorkPaneViewModel : ViewModelBase
{
    private readonly Func<Task> _focus;
    private readonly IWorkerRoutingStore? _store;
    private readonly AgentRuntimeRegistry? _runtimes;
    private readonly IAgentInteractiveSessionLauncher? _interactiveLauncher;
    private readonly WorkerRemovalService? _removalService;
    private readonly TaskRevisionRepository? _taskRevisions;
    private readonly WorkerSessionRouter? _workerRouter;
    private readonly CoreProject? _project;
    public WorkPaneViewModel(Func<Task> focus, IWorkerRoutingStore? store = null, AgentRuntimeRegistry? runtimes = null, IAgentInteractiveSessionLauncher? interactiveLauncher = null, WorkerRemovalService? removalService = null, TaskRevisionRepository? taskRevisions = null, WorkerSessionRouter? workerRouter = null, CoreProject? project = null) { _focus = focus; _store = store; _runtimes = runtimes; _interactiveLauncher = interactiveLauncher; _taskRevisions = taskRevisions; _workerRouter = workerRouter; _project = project; _removalService = removalService ?? (store is not null && runtimes is not null ? new WorkerRemovalService(runtimes, store, TimeProvider.System) : null); }
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
    public async Task LoadAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        Workers.Clear(); if (_store is null) return;
        var sessions = await _store.ListSessionsAsync(projectId, cancellationToken);
        foreach (var session in sessions.OrderBy(item => Rank(item.Session.Status)).ThenByDescending(item => item.LastActiveAt))
        {
            var item = await ReconcileStatusAsync(session, cancellationToken);
            item = await ReconcileStatusAsync(item, cancellationToken);
            var plan = await LoadPlanAsync(item, cancellationToken);
            Workers.Add(new WorkerSessionCardViewModel(item, plan));
        }
        OnPropertyChanged(nameof(HasWorkers));
    }

    private async Task<WorkerSessionRecord> ReconcileStatusAsync(WorkerSessionRecord worker, CancellationToken cancellationToken)
    {
        if (_store is null || _runtimes is null || worker.Session.Status is not (AgentSessionStatus.Running or AgentSessionStatus.WaitingApproval)) return worker;
        if (worker.LastActiveAt > DateTimeOffset.UtcNow.AddSeconds(-2)) return worker;
        IAgentRuntime runtime;
        try { runtime = _runtimes.GetByAccount(worker.Session.AccountId); }
        catch (KeyNotFoundException) { return worker; }
        AgentSessionStatus observed;
        try { observed = await runtime.GetStatusAsync(worker.Session, cancellationToken); }
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
        SelectedWorker = worker; Transcript.Clear(); if (_runtimes is null) return;
        var runtime = _runtimes.GetByAccount(worker.Session.AccountId);
        var events = await runtime.GetTranscriptAsync(worker.Session, cancellationToken);
        foreach (var item in events.OfType<AgentMessage>()) Transcript.Add(new WorkerTranscriptLineViewModel(item.Role.ToString(), item.Text));
    }
    public async Task OpenWorkerAsync(WorkerSessionCardViewModel worker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(worker);
        OpenError = null;
        if (_interactiveLauncher is null || !_interactiveLauncher.CanOpen(worker.Record))
        {
            OpenError = LocalizationService.Current["Dynamic.WorkerRuntimeUnsupported"];
            return;
        }

        var result = await _interactiveLauncher.OpenAsync(worker.Record, cancellationToken);
        OpenError = result.Error;
    }

    public Task ActivateWorkerCardAsync(WorkerSessionCardViewModel worker, CancellationToken cancellationToken = default) =>
        OpenWorkerAsync(worker, cancellationToken);

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
    private static int Rank(AgentSessionStatus status) => status switch { AgentSessionStatus.Running => 0, AgentSessionStatus.WaitingApproval => 1, AgentSessionStatus.Ready => 2, AgentSessionStatus.Interrupted or AgentSessionStatus.Failed => 3, AgentSessionStatus.Completed => 4, _ => 5 };
}
public sealed class WorkerSessionCardViewModel
{
    private readonly WorkerSessionRecord record;
    public WorkerSessionCardViewModel(WorkerSessionRecord record, IReadOnlyList<string>? plan = null)
    {
        this.record = record;
        PlanSteps = BuildPlan(plan ?? []);
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
    public IReadOnlyList<WorkerPlanStepViewModel> PlanSteps { get; }
    public bool HasPlan => PlanSteps.Count > 0;
    public string ProgressText => !HasPlan ? string.Empty : record.Session.Status == AgentSessionStatus.Completed ? $"{PlanSteps.Count}/{PlanSteps.Count} · 已完成" : $"{PlanSteps.Count(step => step.Marker == "✓")}/{PlanSteps.Count}";
    public string CurrentPlanStep => PlanSteps.FirstOrDefault(step => step.IsCurrent)?.Text ?? string.Empty;

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

    public string Marker { get; }
    public string Text { get; }
    public bool IsCurrent { get; }

    private double _opacity;
    public double Opacity
    {
        get => _opacity;
        private set => SetProperty(ref _opacity, value);
    }

    internal void SetPulse(double opacity) => Opacity = IsCurrent ? opacity : 0.86d;
}
public sealed record WorkerTranscriptLineViewModel(string Role, string Text);
