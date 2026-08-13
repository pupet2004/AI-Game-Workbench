using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.App.Worker;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;

namespace Workbench.App.ViewModels.Panes;

public sealed partial class WorkPaneViewModel : ViewModelBase
{
    private readonly Func<Task> _focus;
    private readonly IWorkerRoutingStore? _store;
    private readonly AgentRuntimeRegistry? _runtimes;
    public WorkPaneViewModel(Func<Task> focus, IWorkerRoutingStore? store = null, AgentRuntimeRegistry? runtimes = null) { _focus = focus; _store = store; _runtimes = runtimes; }
    public ObservableCollection<WorkerSessionCardViewModel> Workers { get; } = [];
    public ObservableCollection<WorkerTranscriptLineViewModel> Transcript { get; } = [];
    public bool HasWorkers => Workers.Count > 0;
    [ObservableProperty] public partial WorkerSessionCardViewModel? SelectedWorker { get; set; }
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
    [RelayCommand] private Task OpenWorker(WorkerSessionCardViewModel worker) => SelectWorkerAsync(worker);
    [RelayCommand] private Task Focus() => _focus();
    private static int Rank(AgentSessionStatus status) => status switch { AgentSessionStatus.Running or AgentSessionStatus.Ready => 0, AgentSessionStatus.Interrupted or AgentSessionStatus.Failed => 1, AgentSessionStatus.Completed => 2, _ => 3 };
}
public sealed class WorkerSessionCardViewModel(WorkerSessionRecord record)
{
    public string TaskTitle => record.TaskTitle; public string WorkerLabel => record.Label; public string Profile => record.Profile.ModelProfileId; public AgentSession Session => record.Session;
    public string Status => record.Session.Status switch { AgentSessionStatus.Running or AgentSessionStatus.Ready => "Working", AgentSessionStatus.Completed => "Completed", AgentSessionStatus.Interrupted or AgentSessionStatus.Failed => "Interrupted", AgentSessionStatus.Stopped or AgentSessionStatus.Archived => "Closed", _ => record.Session.Status.ToString() };
    public string LastActiveAtText => record.LastActiveAt.LocalDateTime.ToString("g");
}
public sealed record WorkerTranscriptLineViewModel(string Role, string Text);
