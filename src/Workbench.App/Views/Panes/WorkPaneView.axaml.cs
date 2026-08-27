using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Threading;
using Workbench.App.ViewModels.Panes;

namespace Workbench.App.Views.Panes;

public partial class WorkPaneView : UserControl
{
    private readonly DispatcherTimer _progressPulseTimer;
    private double _pulsePhase;

    public WorkPaneView()
    {
        InitializeComponent();
        AddHandler(InputElement.PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _progressPulseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _progressPulseTimer.Tick += OnProgressPulseTick;
        _progressPulseTimer.Start();
    }

    private void OnProgressPulseTick(object? sender, EventArgs e)
    {
        _pulsePhase += 0.08d;
        var opacity = 0.58d + (Math.Sin(_pulsePhase * Math.PI * 2d) + 1d) * 0.18d;
        if (DataContext is not WorkPaneViewModel pane) return;
        foreach (var worker in pane.Workers)
            foreach (var step in worker.PlanSteps)
                step.SetPulse(opacity);
    }

    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed ||
            e.Source is not Visual source ||
            FindWorkerCard(source) is not { } card ||
            DataContext is not WorkPaneViewModel pane)
        {
            return;
        }

        OpenWorkerMenu(card.Target, pane, card.Worker);
        e.Handled = true;
    }

    private static (Control Target, WorkerSessionCardViewModel Worker)? FindWorkerCard(Visual source)
    {
        for (Visual? current = source; current is not null; current = current.GetVisualParent())
        {
            if (current is Control control && control.DataContext is WorkerSessionCardViewModel worker)
                return (control, worker);
        }

        return null;
    }

    private static void OpenWorkerMenu(Control target, WorkPaneViewModel pane, WorkerSessionCardViewModel worker)
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "重新检查状态", Command = pane.RefreshWorkerStatusCommand, CommandParameter = worker });
        menu.Items.Add(new MenuItem { Header = "标记为已结束", Command = pane.MarkWorkerCompletedCommand, CommandParameter = worker });
        menu.Items.Add(new MenuItem { Header = "查看详情", Command = pane.RequestWorkerDetailsCommand, CommandParameter = worker });
        menu.Items.Add(new MenuItem { Header = "从 Workbench 移除", Command = pane.RequestWorkerRemovalCommand, CommandParameter = worker });
        menu.Open(target);
    }

    private void OnWorkerActionsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: WorkerSessionCardViewModel worker } target ||
            DataContext is not WorkPaneViewModel pane)
        {
            return;
        }

        OpenWorkerMenu(target, pane, worker);
        e.Handled = true;
    }

    private async void OnWorkerCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            e.Source is Visual source && (source is Button || source.FindAncestorOfType<Button>() is not null) ||
            sender is not Border { DataContext: WorkerSessionCardViewModel leftWorker } ||
            DataContext is not WorkPaneViewModel leftPane)
        {
            return;
        }

        e.Handled = true;
        await leftPane.ActivateWorkerCardAsync(leftWorker);
    }
}
