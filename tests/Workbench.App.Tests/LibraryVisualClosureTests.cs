using Workbench.App.ViewModels.Panes;
using Workbench.Project.Git;
using Workbench.Project.Opening;
using Workbench.Core.Layout;

namespace Workbench.App.Tests;

public sealed class LibraryVisualClosureTests
{
    [Fact]
    public void Library_without_b1_state_keeps_legacy_category_view()
    {
        var project = new Workbench.Core.Projects.Project(Guid.NewGuid(), "Project", "C:/Project", Workbench.Core.Projects.ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var pane = new LibraryPaneViewModel(new ProjectOpenResult(project, ProjectLayout.CreateDefault(project.Id), new GitSnapshot(false, false, null, null, null, false, false, null)));

        Assert.True(pane.HasCategoryView);
    }

    [Fact]
    public void Library_markup_hides_legacy_memory_and_does_not_constrain_selector_button_width()
    {
        var markup = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Workbench.App", "Views", "Panes", "LibraryPaneView.axaml"));

        Assert.Contains("Content=\"Overview\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Project Memory", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Memory learning", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Pending Candidates", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemWidth=\"68\"", markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"Category\"", markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"Time\"", markup, StringComparison.Ordinal);
        Assert.Contains("Content=\"Project\"", markup, StringComparison.Ordinal);
        Assert.Contains("Text=\"↑ old → new\"", markup, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AI.Game.Workbench.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
