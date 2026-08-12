using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Workbench.App.Services;
using Workbench.App.ViewModels;
using Workbench.App.Views;

namespace Workbench.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var viewModel = new MainWindowViewModel(
                AppServices.CreateDefault(),
                new FolderPickerService(window));
            window.DataContext = viewModel;
            window.Closing += (_, _) =>
            {
                if (viewModel.CurrentPage is WorkspaceViewModel workspace)
                {
                    _ = workspace.FlushLayoutAsync();
                }
            };
            desktop.MainWindow = window;
            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
