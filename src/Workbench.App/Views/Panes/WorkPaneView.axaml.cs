using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia;
using Workbench.App.ViewModels.Panes;

namespace Workbench.App.Views.Panes;

public partial class WorkPaneView : UserControl
{
    public WorkPaneView() => InitializeComponent();

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
