using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Specialized;
using Workbench.App.ViewModels.Panes;

namespace Workbench.App.Views.Panes;

public partial class LeaderPaneView : UserControl
{
    private readonly DispatcherTimer _runtimeStatusTimer;
    private LeaderPaneViewModel? _statusPane;
    private int _workingDots;

    public LeaderPaneView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        _runtimeStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _runtimeStatusTimer.Tick += OnRuntimeStatusTimerTick;
        _runtimeStatusTimer.Start();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => SubscribeToAnchorRequests();

    private void OnDataContextChanged(object? sender, EventArgs e) => SubscribeToAnchorRequests();

    private void SubscribeToAnchorRequests()
    {
        if (_statusPane is not null)
        {
            _statusPane.PropertyChanged -= OnStatusPanePropertyChanged;
            _statusPane.Activities.CollectionChanged -= OnActivitiesChanged;
        }

        if (DataContext is not LeaderPaneViewModel pane)
        {
            _statusPane = null;
            UpdateRuntimeStatusText();
            return;
        }

        _statusPane = pane;
        _statusPane.PropertyChanged += OnStatusPanePropertyChanged;
        _statusPane.Activities.CollectionChanged += OnActivitiesChanged;
        pane.PropertyChanged -= OnPanePropertyChanged;
        pane.PropertyChanged += OnPanePropertyChanged;
        if (pane.InitialAnchorRequestVersion > 0) QueueInitialAnchor();
        UpdateRuntimeStatusText();
    }

    private void OnStatusPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LeaderPaneViewModel.RuntimeStatus))
        {
            UpdateRuntimeStatusText();
        }
    }

    private void OnActivitiesChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateRuntimeStatusText();

    private void OnRuntimeStatusTimerTick(object? sender, EventArgs e)
    {
        if (string.Equals(_statusPane?.RuntimeStatus, "Working", StringComparison.Ordinal))
        {
            _workingDots = (_workingDots % 3) + 1;
            UpdateRuntimeStatusText();
        }
    }

    private void UpdateRuntimeStatusText()
    {
        var status = _statusPane?.RuntimeStatus;
        var currentActivity = _statusPane?.Activities.LastOrDefault(item =>
            !item.IsComplete && IsRenderableActivity(item.Title));
        RuntimeStatusText.Text = string.Equals(status, "Working", StringComparison.Ordinal)
            ? $"Working{(currentActivity?.Title is { Length: > 0 } title ? $" · {title}" : string.Empty)}{new string('.', _workingDots)}"
            : status;
    }

    private static bool IsRenderableActivity(string? title) =>
        !string.IsNullOrWhiteSpace(title) && title.Trim().ToLowerInvariant() is not
            ("usermessage" or "assistantmessage" or "turnstarted" or "turncompleted" or "turn/started" or "turn/completed");

    private void OnPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LeaderPaneViewModel.InitialAnchorRequestVersion)) QueueInitialAnchor();
    }

    private void QueueInitialAnchor() =>
        Dispatcher.UIThread.Post(ConversationScrollViewer.ScrollToEnd, DispatcherPriority.Loaded);
}
