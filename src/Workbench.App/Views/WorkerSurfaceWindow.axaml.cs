using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Workbench.App.AgentHost;

namespace Workbench.App.Views;

public partial class WorkerSurfaceWindow : Window
{
    private HostedAgentSurfaceViewModel Surface => (HostedAgentSurfaceViewModel)DataContext!;

    public WorkerSurfaceWindow()
    {
        InitializeComponent();
    }

    public WorkerSurfaceWindow(HostedAgentSurfaceViewModel surface)
        : this()
    {
        DataContext = surface ?? throw new ArgumentNullException(nameof(surface));
    }

    private async void OnSendClick(object? sender, RoutedEventArgs e) => await SendCurrentAsync();

    private async void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || (e.KeyModifiers & KeyModifiers.Shift) != 0) return;
        e.Handled = true;
        await SendCurrentAsync();
    }

    private async Task SendCurrentAsync()
    {
        var text = Surface.DraftText.Trim();
        if (text.Length == 0) return;
        Surface.DraftText = string.Empty;
        await Surface.SendAsync(text);
    }

    private async void OnStopClick(object? sender, RoutedEventArgs e) => await Surface.StopAsync();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Hide();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // The window is a disposable Surface. Closing it must not terminate
        // the hosted Worker Session.
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
