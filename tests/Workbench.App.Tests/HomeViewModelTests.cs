using Workbench.App.Services;
using Workbench.App.Tests.Support;
using Workbench.App.ViewModels;
using Workbench.Core.Projects;
using Workbench.Project.Git;
using Workbench.Project.Opening;

namespace Workbench.App.Tests;

public sealed class HomeViewModelTests
{
    [Fact]
    public async Task Loads_recent_projects_ordered_by_last_opened()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var oldest = new TemporaryDirectory("oldest");
        using var newest = new TemporaryDirectory("newest");
        await context.Services.ProjectOpenService.OpenAsync(oldest.Path);
        context.Time.SetUtcNow(DateTimeOffset.Parse("2026-02-01T00:00:00.0000000+00:00"));
        await context.Services.ProjectOpenService.OpenAsync(newest.Path);
        var home = context.CreateHome();

        await home.LoadAsync();

        Assert.Equal(["newest", "oldest"], home.RecentProjects.Select(item => item.Name));
    }

    [Fact]
    public async Task Empty_state_when_no_projects_exist()
    {
        await using var context = await AppTestContext.CreateAsync();
        var home = context.CreateHome();

        await home.LoadAsync();

        Assert.Empty(home.RecentProjects);
        Assert.True(home.HasNoRecentProjects);
    }

    [Fact]
    public async Task Marks_missing_project_path_as_unavailable()
    {
        using var folder = new TemporaryDirectory("gone");
        await using var context = await AppTestContext.CreateAsync();
        await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        folder.Dispose();
        var home = context.CreateHome();

        await home.LoadAsync();

        Assert.Equal(ProjectAvailability.PathMissing, Assert.Single(home.RecentProjects).Availability);
    }

    [Fact]
    public async Task Available_project_can_be_opened()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await AppTestContext.CreateAsync();
        var home = context.CreateHome();

        await home.OpenPathAsync(folder.Path);

        Assert.NotNull(context.LastOpened);
        Assert.Equal(Path.GetFullPath(folder.Path), context.LastOpened!.Project.RootPath);
    }

    [Fact]
    public async Task Unavailable_project_cannot_be_opened_directly()
    {
        using var folder = new TemporaryDirectory("gone");
        await using var context = await AppTestContext.CreateAsync();
        await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        folder.Dispose();
        var home = context.CreateHome();
        await home.LoadAsync();

        await home.OpenRecentProjectAsync(Assert.Single(home.RecentProjects));

        Assert.Null(context.LastOpened);
    }

    [Fact]
    public async Task Opening_project_refreshes_recent_list()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await AppTestContext.CreateAsync();
        var home = context.CreateHome();

        await home.OpenPathAsync(folder.Path);

        Assert.Single(home.RecentProjects);
    }

    [Fact]
    public async Task Restarting_app_loads_persisted_recent_project()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await AppTestContext.CreateAsync();
        await context.CreateHome().OpenPathAsync(folder.Path);
        var restartedServices = AppServices.CreateForDatabasePath(context.Services.Database.DatabasePath, context.Time);
        await restartedServices.InitializeAsync();
        var restartedHome = new HomeViewModel(
            restartedServices.ProjectRepository,
            restartedServices.ProjectOpenService,
            new TestFolderPickerService(null),
            _ => Task.CompletedTask);

        await restartedHome.LoadAsync();

        Assert.Equal(Path.GetFullPath(folder.Path), Assert.Single(restartedHome.RecentProjects).RootPath);
    }

    [Fact]
    public async Task Opening_existing_project_reuses_existing_project_identity()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await AppTestContext.CreateAsync();
        var home = context.CreateHome();
        await home.OpenPathAsync(folder.Path);
        var firstId = context.LastOpened!.Project.Id;
        context.Time.SetUtcNow(DateTimeOffset.Parse("2026-02-01T00:00:00.0000000+00:00"));

        await home.OpenPathAsync(folder.Path);

        Assert.Equal(firstId, context.LastOpened!.Project.Id);
    }

    [Fact]
    public async Task Removing_project_record_does_not_delete_local_folder()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await AppTestContext.CreateAsync();
        await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var home = context.CreateHome();
        await home.LoadAsync();
        var item = Assert.Single(home.RecentProjects);

        home.RequestProjectRemovalCommand.Execute(item);
        await home.ConfirmProjectRemovalCommand.ExecuteAsync(null);

        Assert.Empty(home.RecentProjects);
        Assert.True(Directory.Exists(folder.Path));
        Assert.Null(await context.Services.ProjectRepository.GetByIdAsync(item.Project.Id));
    }

    [Fact]
    public async Task Open_failure_keeps_home_active()
    {
        await using var context = await AppTestContext.CreateAsync();
        var home = context.CreateHome();

        await home.OpenPathAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.Null(context.LastOpened);
        Assert.Equal("Could not open this project. The folder is no longer available; nothing was changed.", home.ErrorMessage);
    }

    [Fact]
    public async Task Busy_state_prevents_duplicate_open()
    {
        await using var context = await AppTestContext.CreateAsync();
        var completion = new TaskCompletionSource<ProjectOpenResult>();
        var calls = 0;
        var home = context.CreateHome((_, _) =>
        {
            calls++;
            return completion.Task;
        });

        var first = home.OpenPathAsync("C:/Project");
        await Task.Yield();
        var second = home.OpenPathAsync("C:/Project");
        completion.SetResult(CreateResult());
        await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cancel_folder_selection_does_nothing()
    {
        await using var context = await AppTestContext.CreateAsync(folderPath: null);
        var home = context.CreateHome();

        await home.OpenProjectFolderAsync();

        Assert.Null(context.LastOpened);
        Assert.Empty(home.RecentProjects);
    }

    private static ProjectOpenResult CreateResult()
    {
        var project = new Workbench.Core.Projects.Project(Guid.NewGuid(), "Project", "C:/Project", ProjectType.Generic, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var layout = Workbench.Core.Layout.ProjectLayout.CreateDefault(project.Id);
        return new ProjectOpenResult(project, layout, new GitSnapshot(true, false, null, null, null, false, false, null));
    }
}
