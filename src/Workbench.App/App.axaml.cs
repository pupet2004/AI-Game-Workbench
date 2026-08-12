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
            var services = AppServices.CreateDefault(CodexRuntimeComposition.ConnectAsync);
            var viewModel = new MainWindowViewModel(
                services,
                new FolderPickerService(window));
            window.DataContext = viewModel;
            var shutdownStarted = false;
            var shutdownComplete = false;
            window.Closing += async (_, args) =>
            {
                if (shutdownComplete)
                {
                    return;
                }

                args.Cancel = true;
                if (shutdownStarted)
                {
                    return;
                }

                shutdownStarted = true;
                try
                {
                    await viewModel.DisposeAsync();
                }
                finally
                {
                    shutdownComplete = true;
                    window.Close();
                }
            };
            desktop.MainWindow = window;
            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
