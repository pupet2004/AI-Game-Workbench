using Workbench.App.Tests.Support;
using Workbench.App.ViewModels;

namespace Workbench.App.Tests;

public sealed class NavigationTests
{
    [Fact]
    public async Task App_starts_on_home()
    {
        await using var context = await AppTestContext.CreateAsync();
        var main = context.CreateMain();
        await main.InitializeAsync();

        Assert.IsType<HomeViewModel>(main.CurrentPage);
    }

    [Fact]
    public async Task Successful_project_open_navigates_to_workspace()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await AppTestContext.CreateAsync();
        var main = context.CreateMain();
        await main.InitializeAsync();

        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(folder.Path);

        Assert.IsType<WorkspaceViewModel>(main.CurrentPage);
    }

    [Fact]
    public async Task Back_to_projects_refreshes_recent_list()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await AppTestContext.CreateAsync();
        var main = context.CreateMain();
        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(folder.Path);

        await main.BackToHomeAsync();

        var home = Assert.IsType<HomeViewModel>(main.CurrentPage);
        Assert.Equal(Path.GetFullPath(folder.Path), Assert.Single(home.RecentProjects).RootPath);
    }
}
