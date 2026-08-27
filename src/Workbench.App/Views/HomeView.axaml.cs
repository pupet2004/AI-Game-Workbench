using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Workbench.App.ViewModels;

namespace Workbench.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        AddHandler(InputElement.PointerPressedEvent, OnProjectPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnProjectPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed ||
            e.Source is not Visual source ||
            (source as Button ?? source.FindAncestorOfType<Button>()) is not { DataContext: RecentProjectItemViewModel project } target ||
            DataContext is not HomeViewModel home ||
            !project.IsAvailable)
        {
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "查看详情", Command = home.RequestProjectDetailsCommand, CommandParameter = project });
        menu.Items.Add(new MenuItem { Header = "删除 Workbench 记录", Command = home.RequestProjectRemovalCommand, CommandParameter = project });
        menu.Open(target);
        e.Handled = true;
    }
}
