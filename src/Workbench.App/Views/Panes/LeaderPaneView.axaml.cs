using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Workbench.App.ViewModels.Panes;

namespace Workbench.App.Views.Panes;

public partial class LeaderPaneView : UserControl
{
    public LeaderPaneView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => SubscribeToAnchorRequests();

    private void OnDataContextChanged(object? sender, EventArgs e) => SubscribeToAnchorRequests();

    private void SubscribeToAnchorRequests()
    {
        if (DataContext is not LeaderPaneViewModel pane) return;
        pane.PropertyChanged -= OnPanePropertyChanged;
        pane.PropertyChanged += OnPanePropertyChanged;
        if (pane.InitialAnchorRequestVersion > 0) QueueInitialAnchor();
    }

    private void OnPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LeaderPaneViewModel.InitialAnchorRequestVersion)) QueueInitialAnchor();
    }

    private void QueueInitialAnchor() =>
        Dispatcher.UIThread.Post(ConversationScrollViewer.ScrollToEnd, DispatcherPriority.Loaded);
}
