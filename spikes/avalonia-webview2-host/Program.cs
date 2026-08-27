using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;

namespace AvaloniaWebView2Host;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}

internal sealed class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class MainWindow : Window
{
    public MainWindow()
    {
        Title = "Avalonia WebView2 Host Probe";
        Width = 900;
        Height = 600;
        var web = new WebView2 { HtmlSource = "<html><body><h1>WebView2.Avalonia probe</h1><script>window.chrome.webview.postMessage('ready')</script></body></html>" };
        web.WebMessageReceived += (_, args) => Title = $"Message: {args.TryGetWebMessageAsString()}";
        Content = web;
    }
}
