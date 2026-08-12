using Workbench.Core.Layout;

namespace Workbench.Core.Tests.Layout;

public sealed class ProjectLayoutTests
{
    [Fact]
    public void Default_layout_has_expected_ratios()
    {
        var layout = ProjectLayout.CreateDefault(Guid.NewGuid());

        AssertRatios(layout, 0.30, 0.45, 0.25);
        Assert.Equal(WorkspacePane.Work, layout.FocusedPane);
    }

    [Fact]
    public void Leader_focus_has_expected_ratios()
    {
        AssertRatios(ProjectLayout.FocusLeader(Guid.NewGuid()), 0.60, 0.25, 0.15);
    }

    [Fact]
    public void Work_focus_has_expected_ratios()
    {
        AssertRatios(ProjectLayout.FocusWork(Guid.NewGuid()), 0.15, 0.70, 0.15);
    }

    [Fact]
    public void Library_focus_has_expected_ratios()
    {
        AssertRatios(ProjectLayout.FocusLibrary(Guid.NewGuid()), 0.15, 0.20, 0.65);
    }

    [Fact]
    public void Invalid_negative_width_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectLayout(Guid.NewGuid(), -0.1, 0.6, 0.5, WorkspacePane.Work, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Invalid_zero_width_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectLayout(Guid.NewGuid(), 0, 0.6, 0.4, WorkspacePane.Work, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Invalid_total_width_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ProjectLayout(Guid.NewGuid(), 0.3, 0.3, 0.3, WorkspacePane.Work, DateTimeOffset.UtcNow));
    }

    private static void AssertRatios(ProjectLayout layout, double leader, double work, double library)
    {
        Assert.Equal(leader, layout.LeaderWidth);
        Assert.Equal(work, layout.WorkWidth);
        Assert.Equal(library, layout.LibraryWidth);
    }
}
