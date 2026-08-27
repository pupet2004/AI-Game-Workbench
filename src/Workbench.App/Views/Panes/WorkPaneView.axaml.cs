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

    private void OnWorkerCardContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Border { DataContext: WorkerSessionCardViewModel worker } target ||
            DataContext is not WorkPaneViewModel pane)
        {
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "重新检查状态", Command = pane.RefreshWorkerStatusCommand, CommandParameter = worker });
        menu.Items.Add(new MenuItem { Header = "标记为已结束", Command = pane.MarkWorkerCompletedCommand, CommandParameter = worker });
        menu.Items.Add(new MenuItem { Header = "查看详情", Command = pane.RequestWorkerDetailsCommand, CommandParameter = worker });
        menu.Items.Add(new MenuItem { Header = "从 Workbench 移除", Command = pane.RequestWorkerRemovalCommand, CommandParameter = worker });
        menu.Open(target);
        e.Handled = true;
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
