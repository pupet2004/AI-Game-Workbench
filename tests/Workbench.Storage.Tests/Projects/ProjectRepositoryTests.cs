using Workbench.Core.Projects;
using Workbench.Storage.Database;
using Workbench.Storage.Projects;
using Workbench.Storage.Tests.Database;

namespace Workbench.Storage.Tests.Projects;

public sealed class ProjectRepositoryTests
{
    [Fact]
    public async Task Project_round_trips()
    {
        await using var temporary = new TemporaryDatabase();
        var repository = await CreateRepositoryAsync(temporary.DatabasePath);
        var project = CreateProject(name: "Demo", rootPath: "C:/Projects/Demo");

        await repository.UpsertAsync(project);
        var restored = await repository.GetByIdAsync(project.Id);

        Assert.Equal(project, restored);
    }

    [Fact]
    public async Task Project_allows_null_git_root_round_trip()
    {
        await using var temporary = new TemporaryDatabase();
        var repository = await CreateRepositoryAsync(temporary.DatabasePath);
        var project = CreateProject(gitRoot: null);

        await repository.UpsertAsync(project);

        Assert.Null((await repository.GetByIdAsync(project.Id))!.GitRoot);
    }

    [Fact]
    public async Task Project_type_round_trips()
    {
        await using var temporary = new TemporaryDatabase();
        var repository = await CreateRepositoryAsync(temporary.DatabasePath);
        var project = CreateProject(projectType: ProjectType.Unreal);

        await repository.UpsertAsync(project);

        Assert.Equal(ProjectType.Unreal, (await repository.GetByIdAsync(project.Id))!.Type);
    }

    [Fact]
    public async Task Project_dates_round_trip_as_DateTimeOffset()
    {
        await using var temporary = new TemporaryDatabase();
        var repository = await CreateRepositoryAsync(temporary.DatabasePath);
        var createdAt = new DateTimeOffset(2026, 4, 5, 12, 13, 14, TimeSpan.FromHours(8));
        var lastOpenedAt = new DateTimeOffset(2026, 4, 6, 15, 16, 17, TimeSpan.FromHours(-4));
        var project = CreateProject(createdAt: createdAt, lastOpenedAt: lastOpenedAt);

        await repository.UpsertAsync(project);
        var restored = await repository.GetByIdAsync(project.Id);

        Assert.Equal(createdAt, restored!.CreatedAt);
        Assert.Equal(lastOpenedAt, restored.LastOpenedAt);
    }

    [Fact]
    public async Task Get_by_root_path_returns_project()
    {
        await using var temporary = new TemporaryDatabase();
        var repository = await CreateRepositoryAsync(temporary.DatabasePath);
        var project = CreateProject(rootPath: "C:/Projects/Demo");
        await repository.UpsertAsync(project);

        var restored = await repository.GetByRootPathAsync("c:/projects/demo");

        Assert.Equal(project, restored);
    }

    [Fact]
    public async Task Recent_projects_are_sorted_by_last_opened()
    {
        await using var temporary = new TemporaryDatabase();
        var repository = await CreateRepositoryAsync(temporary.DatabasePath);
        var oldest = CreateProject(rootPath: "C:/Projects/Oldest", lastOpenedAt: DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00"));
        var newest = CreateProject(rootPath: "C:/Projects/Newest", lastOpenedAt: DateTimeOffset.Parse("2026-03-01T00:00:00.0000000+00:00"));
        var middle = CreateProject(rootPath: "C:/Projects/Middle", lastOpenedAt: DateTimeOffset.Parse("2026-02-01T00:00:00.0000000+00:00"));
        await repository.UpsertAsync(oldest);
        await repository.UpsertAsync(newest);
        await repository.UpsertAsync(middle);

        var projects = await repository.GetRecentAsync(3);

        Assert.Equal([newest.Id, middle.Id, oldest.Id], projects.Select(project => project.Id));
    }

    [Fact]
    public async Task Recent_projects_respect_limit()
    {
        await using var temporary = new TemporaryDatabase();
        var repository = await CreateRepositoryAsync(temporary.DatabasePath);
        await repository.UpsertAsync(CreateProject(rootPath: "C:/Projects/One"));
        await repository.UpsertAsync(CreateProject(rootPath: "C:/Projects/Two"));

        var projects = await repository.GetRecentAsync(1);

        Assert.Single(projects);
    }

    [Fact]
    public async Task Invalid_recent_limit_is_rejected()
    {
        await using var temporary = new TemporaryDatabase();
        var repository = await CreateRepositoryAsync(temporary.DatabasePath);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.GetRecentAsync(0));
    }

    [Fact]
    public async Task Same_project_id_upserts_existing_row()
    {
        await using var temporary = new TemporaryDatabase();
        var repository = await CreateRepositoryAsync(temporary.DatabasePath);
        var project = CreateProject(name: "Before");
        await repository.UpsertAsync(project);

        await repository.UpsertAsync(project with { Name = "After" });
        var projects = await repository.GetRecentAsync(10);

        Assert.Single(projects);
        Assert.Equal("After", projects[0].Name);
    }

    [Fact]
    public async Task Different_project_id_with_same_root_path_is_rejected()
    {
        await using var temporary = new TemporaryDatabase();
        var repository = await CreateRepositoryAsync(temporary.DatabasePath);
        await repository.UpsertAsync(CreateProject(rootPath: "C:/Projects/Demo"));

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => repository.UpsertAsync(CreateProject(rootPath: "c:/projects/demo")));
    }

    [Fact]
    public async Task Remove_project_hides_project_and_marks_setup_required()
    {
        await using var temporary = new TemporaryDatabase();
        var repository = await CreateRepositoryAsync(temporary.DatabasePath);
        var project = CreateProject();
        await repository.UpsertAsync(project);

        await repository.RemoveAsync(project.Id);

        Assert.NotNull(await repository.GetByIdAsync(project.Id));
        Assert.Empty(await repository.GetRecentAsync(10));
        Assert.True(await repository.IsSetupRequiredAsync(project.Id));

        await repository.UpsertAsync(project);
        Assert.Single(await repository.GetRecentAsync(10));
        Assert.True(await repository.IsSetupRequiredAsync(project.Id));

        await repository.CompleteSetupAsync(project.Id);
        Assert.False(await repository.IsSetupRequiredAsync(project.Id));
    }

    private static async Task<ProjectRepository> CreateRepositoryAsync(string databasePath)
    {
        var database = new WorkbenchDatabase(databasePath);
        await database.InitializeAsync();
        return new ProjectRepository(database);
    }

    private static Project CreateProject(
        string name = "Project",
        string rootPath = "C:/Projects/Project",
        ProjectType projectType = ProjectType.Godot,
        string? gitRoot = "C:/Projects",
        DateTimeOffset? createdAt = null,
        DateTimeOffset? lastOpenedAt = null) =>
        new(Guid.NewGuid(), name, rootPath, projectType, gitRoot,
            createdAt ?? DateTimeOffset.Parse("2026-01-01T00:00:00.0000000+00:00"),
            lastOpenedAt ?? DateTimeOffset.Parse("2026-01-02T00:00:00.0000000+00:00"));
}
