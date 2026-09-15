using Avalonia.Controls;
using Avalonia.Threading;
using Workbench.App.ProjectWorld;

namespace Workbench.App.Views;

public partial class ProjectWorldExplorerView : UserControl
{
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(5) };

    public ProjectWorldExplorerView()
    {
        InitializeComponent();
        _refresh.Tick += async (_, _) =>
        {
            if (DataContext is ProjectWorldExplorerViewModel { Loading: false, IsWorldView: false } overview)
                await overview.InitializeAsync();
        };
        AttachedToVisualTree += (_, _) => _refresh.Start();
        DetachedFromVisualTree += (_, _) => _refresh.Stop();
    }
}
