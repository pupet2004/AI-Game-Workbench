using Workbench.Core.Layout;
using Workbench.Core.Projects;
using Workbench.Storage.Database;
using Workbench.Storage.Projects;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Projects;

public sealed class ProjectLayoutRepositoryTests
{
    [Fact]
    public async Task Layout_round_trips()
    {
        await using var temporary = new TemporaryDatabase();
        var (projects, layouts) = await CreateRepositoriesAsync(temporary.DatabasePath);
        var project = CreateProject();
        var layout = CreateLayout(project.Id, 0.30, 0.45, 0.25, WorkspacePane.Work);
        await projects.UpsertAsync(project);

        await layouts.SaveAsync(layout);
        var restored = await layouts.GetAsync(project.Id);

        Assert.Equal(layout, restored);
    }

    [Fact]
    public async Task Saving_layout_twice_updates_existing_layout()
    {
        await using var temporary = new TemporaryDatabase();
        var (projects, layouts) = await CreateRepositoriesAsync(temporary.DatabasePath);
        var project = CreateProject();
        await projects.UpsertAsync(project);
        await layouts.SaveAsync(CreateLayout(project.Id, 0.30, 0.45, 0.25, WorkspacePane.Work));
        var updated = CreateLayout(project.Id, 0.60, 0.25, 0.15, WorkspacePane.Leader);

        await layouts.SaveAsync(updated);
        var restored = await layouts.GetAsync(project.Id);

        Assert.Equal(updated, restored);
    }

    [Fact]
    public async Task Different_projects_keep_independent_layouts()
    {
        await using var temporary = new TemporaryDatabase();
        var (projects, layouts) = await CreateRepositoriesAsync(temporary.DatabasePath);
        var first = CreateProject(rootPath: "C:/Projects/First");
        var second = CreateProject(rootPath: "C:/Projects/Second");
        var firstLayout = CreateLayout(first.Id, 0.60, 0.25, 0.15, WorkspacePane.Leader);
        var secondLayout = CreateLayout(second.Id, 0.15, 0.20, 0.65, WorkspacePane.Library);
        await projects.UpsertAsync(first);
        await projects.UpsertAsync(second);

        await layouts.SaveAsync(firstLayout);
        await layouts.SaveAsync(secondLayout);

        Assert.Equal(firstLayout, await layouts.GetAsync(first.Id));
        Assert.Equal(secondLayout, await layouts.GetAsync(second.Id));
    }

    [Fact]
    public async Task Deleting_project_cascades_layout()
    {
        await using var temporary = new TemporaryDatabase();
        var (projects, layouts) = await CreateRepositoriesAsync(temporary.DatabasePath);
        var project = CreateProject();
        await projects.UpsertAsync(project);
        await layouts.SaveAsync(CreateLayout(project.Id, 0.30, 0.45, 0.25, WorkspacePane.Work));

        await projects.RemoveAsync(project.Id);

        Assert.Null(await layouts.GetAsync(project.Id));
    }

    [Fact]
    public async Task Write_project_and_layout_close_database_reopen_and_restore_both()
    {
        await using var temporary = new TemporaryDatabase();
        var project = CreateProject();
        var layout = CreateLayout(project.Id, 0.15, 0.70, 0.15, WorkspacePane.Work);

        var firstDatabase = new WorkbenchDatabase(temporary.DatabasePath);
        await firstDatabase.InitializeAsync();
        var firstProjects = new ProjectRepository(firstDatabase);
        var firstLayouts = new ProjectLayoutRepository(firstDatabase);
        await firstProjects.UpsertAsync(project);
        await firstLayouts.SaveAsync(layout);

        var reopenedDatabase = new WorkbenchDatabase(temporary.DatabasePath);
        await reopenedDatabase.InitializeAsync();
        var reopenedProjects = new ProjectRepository(reopenedDatabase);
        var reopenedLayouts = new ProjectLayoutRepository(reopenedDatabase);

        Assert.Equal(project, await reopenedProjects.GetByIdAsync(project.Id));
        Assert.Equal(layout, await reopenedLayouts.GetAsync(project.Id));
    }

    private static async Task<(ProjectRepository Projects, ProjectLayoutRepository Layouts)> CreateRepositoriesAsync(string databasePath)
    {
        var database = new WorkbenchDatabase(databasePath);
        await database.InitializeAsync();
        return (new ProjectRepository(database), new ProjectLayoutRepository(database));
    }

    private static Project CreateProject(string rootPath = "C:/Projects/Project") =>
        new(Guid.NewGuid(), "Project", rootPath, ProjectType.Godot, null,
            DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00"),
            DateTimeOffset.Parse("2026-01-02T00:00:00.0000000+00:00"));

    private static ProjectLayout CreateLayout(Guid projectId, double leader, double work, double library, WorkspacePane focusedPane) =>
        new(projectId, leader, work, library, focusedPane,
            DateTimeOffset.Parse("2026-01-03T00:00:00.0000000+00:00"));
}
