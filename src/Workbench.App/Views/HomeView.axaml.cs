using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Workbench.App.ViewModels;

namespace Workbench.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private void OnProjectContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Button { DataContext: RecentProjectItemViewModel project } target ||
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
