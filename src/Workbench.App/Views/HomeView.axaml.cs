using Avalonia.Controls;
using Avalonia.Interactivity;
using Workbench.App.ViewModels;

namespace Workbench.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private void OnProjectDetailsClick(object? sender, RoutedEventArgs e) => InvokeProjectCommand(sender, details: true);

    private void OnProjectRemovalClick(object? sender, RoutedEventArgs e) => InvokeProjectCommand(sender, details: false);

    private void InvokeProjectCommand(object? sender, bool details)
    {
        if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: Control target } } menuItem ||
            DataContext is not HomeViewModel home ||
            target.DataContext is not RecentProjectItemViewModel project)
        {
            return;
        }

        if (details)
        {
            home.RequestProjectDetailsCommand.Execute(project);
        }
        else
        {
            home.RequestProjectRemovalCommand.Execute(project);
        }

    }
}
