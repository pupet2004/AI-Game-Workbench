using Avalonia;
using System;

using Workbench.App.SingleInstance;

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
