using Workbench.Core.Layout;
using Workbench.Core.Projects;
using Workbench.Project.Git;
using Workbench.Project.Opening;
using Workbench.Project.Tests.Support;
using Workbench.Storage.Database;
using Workbench.Storage.Projects;

namespace Workbench.Project.Tests.Opening;

public sealed class ProjectOpenServiceTests
{
    [Fact]
    public async Task Opening_new_project_creates_project_record()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await ProjectOpenContext.CreateAsync();

        var result = await context.Service.OpenAsync(folder.Path);

        Assert.Equal(result.Project, await context.Projects.GetByRootPathAsync(Path.GetFullPath(folder.Path)));
    }

    [Fact]
    public async Task Opening_new_project_creates_default_layout()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await ProjectOpenContext.CreateAsync();

        var result = await context.Service.OpenAsync(folder.Path);

        Assert.Equal(0.30, result.Layout.LeaderWidth);
        Assert.Equal(0.45, result.Layout.WorkWidth);
        Assert.Equal(0.25, result.Layout.LibraryWidth);
        Assert.Equal(WorkspacePane.Work, result.Layout.FocusedPane);
    }

    [Fact]
    public async Task Opening_same_path_reuses_project_id()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await ProjectOpenContext.CreateAsync();
        var first = await context.Service.OpenAsync(folder.Path);
        context.Time.SetUtcNow(DateTimeOffset.Parse("2026-02-01T00:00:00.0000000+00:00"));

        var second = await context.Service.OpenAsync(folder.Path);

        Assert.Equal(first.Project.Id, second.Project.Id);
    }

    [Fact]
    public async Task Opening_same_path_preserves_created_at()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await ProjectOpenContext.CreateAsync();
        var first = await context.Service.OpenAsync(folder.Path);
        context.Time.SetUtcNow(DateTimeOffset.Parse("2026-02-01T00:00:00.0000000+00:00"));

        var second = await context.Service.OpenAsync(folder.Path);

        Assert.Equal(first.Project.CreatedAt, second.Project.CreatedAt);
    }

    [Fact]
    public async Task Opening_same_path_updates_last_opened_at()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await ProjectOpenContext.CreateAsync();
        await context.Service.OpenAsync(folder.Path);
        var expected = DateTimeOffset.Parse("2026-02-01T00:00:00.0000000+00:00");
        context.Time.SetUtcNow(expected);

        var second = await context.Service.OpenAsync(folder.Path);

        Assert.Equal(expected, second.Project.LastOpenedAt);
    }

    [Fact]
    public async Task Opening_same_path_refreshes_project_type()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await ProjectOpenContext.CreateAsync();
        var first = await context.Service.OpenAsync(folder.Path);
        File.WriteAllText(Path.Combine(folder.Path, "project.godot"), string.Empty);

        var second = await context.Service.OpenAsync(folder.Path);

        Assert.Equal(ProjectType.Generic, first.Project.Type);
        Assert.Equal(ProjectType.Godot, second.Project.Type);
    }

    [Fact]
    public async Task Opening_git_project_saves_git_root()
    {
        using var folder = new TemporaryDirectory();
        var gitRoot = Path.Combine(folder.Path, "repository");
        await using var context = await ProjectOpenContext.CreateAsync(new GitSnapshot(true, true, gitRoot, "head", "main", false, false, null));

        var result = await context.Service.OpenAsync(folder.Path);

        Assert.Equal(gitRoot, result.Project.GitRoot);
    }

    [Fact]
    public async Task Opening_non_git_project_still_succeeds()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await ProjectOpenContext.CreateAsync();

        var result = await context.Service.OpenAsync(folder.Path);

        Assert.False(result.Git.IsRepository);
        Assert.Null(result.Project.GitRoot);
    }

    [Fact]
    public async Task Opening_missing_directory_is_rejected()
    {
        using var folder = new TemporaryDirectory();
        var missingPath = Path.Combine(folder.Path, "missing");
        await using var context = await ProjectOpenContext.CreateAsync();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => context.Service.OpenAsync(missingPath));

        Assert.Empty(await context.Projects.GetRecentAsync(10));
    }

    [Fact]
    public async Task Different_projects_keep_different_ids()
    {
        using var firstFolder = new TemporaryDirectory("first");
        using var secondFolder = new TemporaryDirectory("second");
        await using var context = await ProjectOpenContext.CreateAsync();

        var first = await context.Service.OpenAsync(firstFolder.Path);
        var second = await context.Service.OpenAsync(secondFolder.Path);

        Assert.NotEqual(first.Project.Id, second.Project.Id);
    }

    [Fact]
    public async Task Use_existing_layout_instead_of_replacing_it()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await ProjectOpenContext.CreateAsync();
        var first = await context.Service.OpenAsync(folder.Path);
        var existingLayout = new ProjectLayout(first.Project.Id, 0.42, 0.43, 0.15, WorkspacePane.Leader, DateTimeOffset.Parse("2026-01-15T00:00:00.0000000+00:00"));
        await context.Layouts.SaveAsync(existingLayout);

        var reopened = await context.Service.OpenAsync(folder.Path);

        Assert.Equal(existingLayout, reopened.Layout);
    }
}

internal sealed class ProjectOpenContext : IAsyncDisposable
{
    private readonly TemporaryDirectory _databaseDirectory;

    private ProjectOpenContext(TemporaryDirectory databaseDirectory, ProjectRepository projects, ProjectLayoutRepository layouts, ProjectOpenService service, MutableTimeProvider time)
    {
        _databaseDirectory = databaseDirectory;
        Projects = projects;
        Layouts = layouts;
        Service = service;
        Time = time;
    }

    public ProjectRepository Projects { get; }

    public ProjectLayoutRepository Layouts { get; }

    public ProjectOpenService Service { get; }

    public MutableTimeProvider Time { get; }

    public static async Task<ProjectOpenContext> CreateAsync(GitSnapshot? snapshot = null)
    {
        var directory = new TemporaryDirectory("database");
        var database = new WorkbenchDatabase(Path.Combine(directory.Path, "workbench.db"));
        await database.InitializeAsync();
        var projects = new ProjectRepository(database);
        var layouts = new ProjectLayoutRepository(database);
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00"));
        var service = new ProjectOpenService(projects, layouts, new StubGitInspector(snapshot ?? new GitSnapshot(true, false, null, null, null, false, false, null)), time);
        return new ProjectOpenContext(directory, projects, layouts, service, time);
    }

    public ValueTask DisposeAsync()
    {
        _databaseDirectory.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class StubGitInspector(GitSnapshot snapshot) : IGitInspector
{
    public Task<GitSnapshot> InspectAsync(string projectPath, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
}

internal sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
}
