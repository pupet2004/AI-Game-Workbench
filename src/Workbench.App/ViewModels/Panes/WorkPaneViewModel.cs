using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.Worker;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;
using Workbench.App.Services;

namespace Workbench.App.ViewModels.Panes;

public sealed partial class WorkPaneViewModel : ViewModelBase
{
    private readonly Func<Task> _focus;
    private readonly IWorkerRoutingStore? _store;
    private readonly AgentRuntimeRegistry? _runtimes;
    private readonly IAgentInteractiveSessionLauncher? _interactiveLauncher;
    private readonly WorkerRemovalService? _removalService;
    public WorkPaneViewModel(Func<Task> focus, IWorkerRoutingStore? store = null, AgentRuntimeRegistry? runtimes = null, IAgentInteractiveSessionLauncher? interactiveLauncher = null, WorkerRemovalService? removalService = null) { _focus = focus; _store = store; _runtimes = runtimes; _interactiveLauncher = interactiveLauncher; _removalService = removalService ?? (store is not null && runtimes is not null ? new WorkerRemovalService(runtimes, store, TimeProvider.System) : null); }
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
        foreach (var item in sessions.OrderBy(item => Rank(item.Session.Status)).ThenByDescending(item => item.LastActiveAt)) Workers.Add(new WorkerSessionCardViewModel(item));
        OnPropertyChanged(nameof(HasWorkers));
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
public sealed class WorkerSessionCardViewModel(WorkerSessionRecord record)
{
    internal WorkerSessionRecord Record => record;
    public string TaskTitle => record.TaskTitle; public string WorkerLabel => record.Label; public string Profile => record.Profile.ModelProfileId; public AgentSession Session => record.Session;
    public string SessionId => record.Session.Id.Value.ToString();
    public string ExternalSessionId => record.Session.ExternalSessionId ?? "未提供";
    public string WorkingDirectory => record.Session.WorkingDirectory ?? "未提供";
    public string Runtime => record.Profile.AgentRuntimeId;
    public string Status => record.Session.Status switch { AgentSessionStatus.Running => LocalizationService.Current["Dynamic.Working"], AgentSessionStatus.WaitingApproval => LocalizationService.Current["Dynamic.Waiting"], AgentSessionStatus.Ready => LocalizationService.Current["Dynamic.Ready"], AgentSessionStatus.Completed => LocalizationService.Current["Dynamic.Completed"], AgentSessionStatus.Interrupted or AgentSessionStatus.Failed => LocalizationService.Current["Dynamic.Interrupted"], AgentSessionStatus.Stopped or AgentSessionStatus.Archived => LocalizationService.Current["Dynamic.Closed"], _ => record.Session.Status.ToString() };
    public string LastActiveAtText => record.LastActiveAt.LocalDateTime.ToString("g");
}
public sealed record WorkerTranscriptLineViewModel(string Role, string Text);
