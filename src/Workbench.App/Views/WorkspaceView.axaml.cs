using Avalonia.Controls;
using Avalonia.Input;
using Workbench.App.ViewModels;

namespace Workbench.App.Views;

public partial class WorkspaceView : UserControl
{
    public WorkspaceView()
    {
        InitializeComponent();
    }

    private void OnDividerDragCompleted(object? sender, VectorEventArgs eventArgs)
    {
        if (DataContext is not WorkspaceViewModel viewModel)
        {
            return;
        }

        viewModel.ApplyPaneWidths(
            WorkspaceGrid.ColumnDefinitions[0].ActualWidth,
            WorkspaceGrid.ColumnDefinitions[2].ActualWidth,
            WorkspaceGrid.ColumnDefinitions[4].ActualWidth);
    }
}
