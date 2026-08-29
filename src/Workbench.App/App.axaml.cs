using Avalonia;
using Avalonia.Controls;
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
            // The Workbench application owns Agent sessions. Closing a window
            // must therefore hide a Surface, not shut down the application.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new MainWindow();
            var services = AppServices.CreateDefault(
                configuredRuntimeFactories:
                [
                    new ConfiguredAgentRuntimeFactory("codex", CodexRuntimeComposition.ConnectAsync),
                    new ConfiguredAgentRuntimeFactory("opencode", OpenCodeRuntimeComposition.ConnectAsync)
                ]);
            var viewModel = new MainWindowViewModel(
                services,
                new FolderPickerService(window));
            window.DataContext = viewModel;
            var shutdownStarted = false;
            var shutdownComplete = false;

            async Task ExitApplicationAsync()
            {
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
                    desktop.Shutdown();
                }
            }

            void ShowMainWindow()
            {
                window.Show();
                window.Activate();
            }

            var trayMenu = new NativeMenu();
            var openItem = new NativeMenuItem("打开 Workbench");
            openItem.Click += (_, _) => ShowMainWindow();
            var exitItem = new NativeMenuItem("退出 Workbench");
            exitItem.Click += (_, _) => _ = ExitApplicationAsync();
            trayMenu.Items.Add(openItem);
            trayMenu.Items.Add(new NativeMenuItemSeparator());
            trayMenu.Items.Add(exitItem);

            var trayIcon = new TrayIcon
            {
                Icon = window.Icon,
                ToolTipText = "AI Game Workbench",
                Menu = trayMenu,
                IsVisible = true
            };
            trayIcon.Clicked += (_, _) => ShowMainWindow();
            var trayIcons = new TrayIcons();
            trayIcons.Add(trayIcon);
            TrayIcon.SetIcons(this, trayIcons);

            window.Closing += (_, args) =>
            {
                if (shutdownComplete)
                {
                    return;
                }

                // A normal window close detaches the main Surface while the
                // application and all hosted Agent sessions remain alive.
                args.Cancel = true;
                window.Hide();
            };
            desktop.MainWindow = window;
            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
