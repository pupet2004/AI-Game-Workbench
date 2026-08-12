using Workbench.App.Tests.Support;
using Workbench.App.ViewModels;
using Workbench.App.ViewModels.Panes;
using Workbench.Core.Layout;
using Workbench.Project.Git;

namespace Workbench.App.Tests;

public sealed class WorkspaceViewModelTests
{
    [Fact]
    public async Task Workspace_uses_project_open_layout()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory();
        var result = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var custom = new ProjectLayout(result.Project.Id, 0.42, 0.43, 0.15, WorkspacePane.Leader, DateTimeOffset.UtcNow);
        await context.Services.ProjectLayoutRepository.SaveAsync(custom);

        var workspace = context.CreateWorkspace(result with { Layout = custom });

        Assert.Equal(custom, workspace.Layout);
    }

    [Fact]
    public async Task Default_project_layout_displays_expected_ratios()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory();
        var workspace = context.CreateWorkspace(await context.Services.ProjectOpenService.OpenAsync(folder.Path));

        Assert.Equal(0.30, workspace.LeaderRatio);
        Assert.Equal(0.45, workspace.WorkRatio);
        Assert.Equal(0.25, workspace.LibraryRatio);
    }

    [Fact]
    public async Task Leader_focus_uses_core_preset()
    {
        await using var context = await AppTestContext.CreateAsync();
        var workspace = await context.CreateWorkspaceForNewProjectAsync();

        await workspace.FocusLeaderAsync();

        Assert.Equal([0.60, 0.25, 0.15], [workspace.LeaderRatio, workspace.WorkRatio, workspace.LibraryRatio]);
    }

    [Fact]
    public async Task Work_focus_uses_core_preset()
    {
        await using var context = await AppTestContext.CreateAsync();
        var workspace = await context.CreateWorkspaceForNewProjectAsync();

        await workspace.FocusWorkAsync();

        Assert.Equal([0.15, 0.70, 0.15], [workspace.LeaderRatio, workspace.WorkRatio, workspace.LibraryRatio]);
    }

    [Fact]
    public async Task Library_focus_uses_core_preset()
    {
        await using var context = await AppTestContext.CreateAsync();
        var workspace = await context.CreateWorkspaceForNewProjectAsync();

        await workspace.FocusLibraryAsync();

        Assert.Equal([0.15, 0.20, 0.65], [workspace.LeaderRatio, workspace.WorkRatio, workspace.LibraryRatio]);
    }

    [Fact]
    public async Task Ordinary_content_action_does_not_change_layout()
    {
        await using var context = await AppTestContext.CreateAsync();
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        var before = workspace.Layout;

        workspace.LeaderPane.RecordContentInteraction();

        Assert.Equal(before, workspace.Layout);
    }

    [Fact]
    public async Task Manual_resize_updates_layout_ratios()
    {
        await using var context = await AppTestContext.CreateAsync();
        var workspace = await context.CreateWorkspaceForNewProjectAsync();

        workspace.ApplyPaneWidths(400, 400, 200);

        Assert.Equal([0.4, 0.4, 0.2], [workspace.LeaderRatio, workspace.WorkRatio, workspace.LibraryRatio]);
    }

    [Fact]
    public void Grid_splitter_drag_completion_is_wired_to_the_layout_bridge()
    {
        var repositoryRoot = FindRepositoryRoot();
        var viewMarkup = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Workbench.App", "Views", "WorkspaceView.axaml"));

        Assert.Equal(2, viewMarkup.Split("DragCompleted=\"OnDividerDragCompleted\"").Length - 1);
        Assert.DoesNotContain("PointerReleased=", viewMarkup);
    }

    [Fact]
    public async Task Manual_resize_rejects_impossible_zero_width_layout()
    {
        await using var context = await AppTestContext.CreateAsync();
        var workspace = await context.CreateWorkspaceForNewProjectAsync();

        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.ApplyPaneWidths(0, 400, 200));
    }

    [Fact]
    public async Task Layout_save_is_debounced()
    {
        await using var context = await AppTestContext.CreateAsync();
        var workspace = await context.CreateWorkspaceForNewProjectAsync(debounce: TimeSpan.FromMilliseconds(30));

        workspace.ApplyPaneWidths(400, 400, 200);
        workspace.ApplyPaneWidths(200, 550, 250);
        await Task.Delay(80);

        var saved = await context.Services.ProjectLayoutRepository.GetAsync(workspace.Result.Project.Id);
        Assert.Equal([0.2, 0.55, 0.25], [saved!.LeaderWidth, saved.WorkWidth, saved.LibraryWidth]);
    }

    [Fact]
    public async Task Focus_layout_is_persisted()
    {
        await using var context = await AppTestContext.CreateAsync();
        var workspace = await context.CreateWorkspaceForNewProjectAsync();

        await workspace.FocusLibraryAsync();

        var saved = await context.Services.ProjectLayoutRepository.GetAsync(workspace.Result.Project.Id);
        Assert.Equal([0.15, 0.20, 0.65], [saved!.LeaderWidth, saved.WorkWidth, saved.LibraryWidth]);
        Assert.Equal(WorkspacePane.Library, saved.FocusedPane);
    }

    [Fact]
    public async Task Reset_layout_restores_core_default()
    {
        await using var context = await AppTestContext.CreateAsync();
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        await workspace.FocusLeaderAsync();

        await workspace.ResetLayoutAsync();

        Assert.Equal(ProjectLayout.CreateDefault(workspace.Result.Project.Id).LeaderWidth, workspace.LeaderRatio);
        Assert.Equal(ProjectLayout.CreateDefault(workspace.Result.Project.Id).WorkWidth, workspace.WorkRatio);
        Assert.Equal(ProjectLayout.CreateDefault(workspace.Result.Project.Id).LibraryWidth, workspace.LibraryRatio);
    }

    [Fact]
    public async Task Existing_custom_layout_is_restored_when_project_reopens()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory();
        var result = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var custom = new ProjectLayout(result.Project.Id, 0.42, 0.43, 0.15, WorkspacePane.Leader, DateTimeOffset.UtcNow);
        await context.Services.ProjectLayoutRepository.SaveAsync(custom);

        var reopened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var workspace = context.CreateWorkspace(reopened);

        Assert.Equal(custom, workspace.Layout);
    }

    [Fact]
    public async Task Different_projects_keep_independent_layouts()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var firstFolder = new TemporaryDirectory("first");
        using var secondFolder = new TemporaryDirectory("second");
        var first = context.CreateWorkspace(await context.Services.ProjectOpenService.OpenAsync(firstFolder.Path));
        var second = context.CreateWorkspace(await context.Services.ProjectOpenService.OpenAsync(secondFolder.Path));
        first.ApplyPaneWidths(400, 400, 200);
        await first.FlushLayoutAsync();
        second.ApplyPaneWidths(200, 550, 250);
        await second.FlushLayoutAsync();

        var reopenedFirst = await context.Services.ProjectOpenService.OpenAsync(firstFolder.Path);
        var reopenedSecond = await context.Services.ProjectOpenService.OpenAsync(secondFolder.Path);

        Assert.Equal([0.4, 0.4, 0.2], [reopenedFirst.Layout.LeaderWidth, reopenedFirst.Layout.WorkWidth, reopenedFirst.Layout.LibraryWidth]);
        Assert.Equal([0.2, 0.55, 0.25], [reopenedSecond.Layout.LeaderWidth, reopenedSecond.Layout.WorkWidth, reopenedSecond.Layout.LibraryWidth]);
    }

    [Fact]
    public async Task Workspace_receives_real_git_snapshot()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory();
        var result = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var workspace = context.CreateWorkspace(result);

        Assert.Equal(result.Git, workspace.Result.Git);
    }

    [Fact]
    public void Library_overview_formats_clean_repo()
    {
        var overview = new LibraryPaneViewModel(CreateResult(new GitSnapshot(true, true, "C:/Repo", "1234567890abcdef", "main", false, false, null)));

        Assert.Equal("Repository", overview.GitStatus);
        Assert.Equal("main", overview.BranchText);
        Assert.Equal("1234567890", overview.ShortHead);
        Assert.Equal("Clean", overview.WorkingTreeText);
    }

    [Fact]
    public void Library_overview_formats_dirty_repo()
    {
        var overview = new LibraryPaneViewModel(CreateResult(new GitSnapshot(true, true, "C:/Repo", "1234567890abcdef", "main", false, true, null)));

        Assert.Equal("Modified", overview.WorkingTreeText);
    }

    [Fact]
    public void Library_overview_formats_detached_head()
    {
        var overview = new LibraryPaneViewModel(CreateResult(new GitSnapshot(true, true, "C:/Repo", "1234567890abcdef", null, true, false, null)));

        Assert.Equal("Detached", overview.BranchText);
    }

    [Fact]
    public void Library_overview_handles_non_git_project()
    {
        var overview = new LibraryPaneViewModel(CreateResult(new GitSnapshot(true, false, null, null, null, false, false, null)));

        Assert.Equal("No Repository", overview.GitStatus);
    }

    [Fact]
    public void Library_overview_handles_git_unavailable()
    {
        var overview = new LibraryPaneViewModel(CreateResult(new GitSnapshot(false, false, null, null, null, false, false, "not found")));

        Assert.Equal("Unavailable", overview.GitStatus);
        Assert.Equal("Git status unavailable", overview.StatusMessage);
    }

    private static Workbench.Project.Opening.ProjectOpenResult CreateResult(GitSnapshot git)
    {
        var project = new Workbench.Core.Projects.Project(Guid.NewGuid(), "Project", "C:/Project", Workbench.Core.Projects.ProjectType.Godot, git.RepositoryRoot, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        return new Workbench.Project.Opening.ProjectOpenResult(project, ProjectLayout.CreateDefault(project.Id), git);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AI.Game.Workbench.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
