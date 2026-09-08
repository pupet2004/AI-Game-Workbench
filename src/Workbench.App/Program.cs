using Avalonia;
using System;

using Workbench.App.SingleInstance;
using Workbench.App.Demo;

namespace Workbench.App;

sealed class Program
{
    internal static SingleInstanceCoordinator? Instance { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "--reset-local-data", StringComparison.Ordinal))
        {
            Workbench.Storage.Database.DatabasePathProvider.ResetDefaultData();
            return;
        }

        if (args.Length >= 2 && string.Equals(args[0], "--bootstrap-demo", StringComparison.Ordinal))
        {
            var projectRoot = Path.GetFullPath(args[1]);
            var databasePath = args.Length >= 3
                ? Path.GetFullPath(args[2])
                : Path.Combine(Path.GetDirectoryName(projectRoot)!, "workbench.db");
            DemoBootstrapper.RunAsync(projectRoot, databasePath).GetAwaiter().GetResult();
            return;
        }

        if (args.Length >= 3 && string.Equals(args[0], "--relocate-demo-project", StringComparison.Ordinal))
        {
            var projectRoot = Path.GetFullPath(args[1]);
            var databasePath = Path.GetFullPath(args[2]);
            var projectName = args.Length >= 4 ? args[3] : "零刻";
            DemoBootstrapper.RelocateProjectAsync(projectRoot, databasePath, projectName)
                .GetAwaiter().GetResult();
            return;
        }

        if (args.Length >= 3 && string.Equals(args[0], "--relocate-demo-project-auto", StringComparison.Ordinal))
        {
            var projectRoot = Path.GetFullPath(args[1]);
            var databasePath = Path.GetFullPath(args[2]);
            DemoBootstrapper.RelocateDemoProjectAutoAsync(projectRoot, databasePath)
                .GetAwaiter().GetResult();
            return;
        }

        if (!SingleInstanceCoordinator.TryAcquirePrimary(out var instance))
        {
            return;
        }

        Instance = instance;
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Instance?.Dispose();
            Instance = null;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
