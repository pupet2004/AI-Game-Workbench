using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia;
using Workbench.App.ViewModels.Panes;

namespace Workbench.App.Views.Panes;

public partial class WorkPaneView : UserControl
{
    public WorkPaneView() => InitializeComponent();

    private void OnWorkerDetailsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: Control target } } menuItem &&
            DataContext is WorkPaneViewModel pane &&
            target.DataContext is WorkerSessionCardViewModel worker)
        {
            pane.RequestWorkerDetailsCommand.Execute(worker);
        }
    }

    private void OnWorkerRemovalClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: Control target } } menuItem &&
            DataContext is WorkPaneViewModel pane &&
            target.DataContext is WorkerSessionCardViewModel worker)
        {
            pane.RequestWorkerRemovalCommand.Execute(worker);
        }
    }

    private async void OnWorkerCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            e.Source is Visual source && (source is Button || source.FindAncestorOfType<Button>() is not null) ||
            sender is not Border { DataContext: WorkerSessionCardViewModel worker } ||
            DataContext is not WorkPaneViewModel pane)
        {
            return;
        }

        e.Handled = true;
        await pane.ActivateWorkerCardAsync(worker);
    }
}
