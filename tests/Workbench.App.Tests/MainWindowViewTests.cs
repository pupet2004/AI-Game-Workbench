namespace Workbench.App.Tests;

public sealed class MainWindowViewTests
{
    [Fact]
    public void Main_window_starts_centered_with_the_existing_safe_default_size()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var markup = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Workbench.App", "Views", "MainWindow.axaml"));

        Assert.Contains("WindowStartupLocation=\"CenterScreen\"", markup, StringComparison.Ordinal);
        Assert.Contains("WindowState=\"Normal\"", markup, StringComparison.Ordinal);
        Assert.Contains("Width=\"1400\"", markup, StringComparison.Ordinal);
        Assert.Contains("Height=\"850\"", markup, StringComparison.Ordinal);
    }
}
