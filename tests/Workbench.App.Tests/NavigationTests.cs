using Workbench.App.ViewModels;
using Workbench.App.Tests.Support;

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
    public async Task Successful_project_open_navigates_to_workspace_placeholder()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await AppTestContext.CreateAsync();
        var main = context.CreateMain();
        await main.InitializeAsync();

        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(folder.Path);

        Assert.IsType<WorkspacePlaceholderViewModel>(main.CurrentPage);
    }

    [Fact]
    public async Task Back_from_workspace_returns_home()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await AppTestContext.CreateAsync();
        var main = context.CreateMain();
        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(folder.Path);

        main.BackToHome();

        Assert.IsType<HomeViewModel>(main.CurrentPage);
    }

    [Fact]
    public async Task Workspace_placeholder_receives_project_open_result()
    {
        using var folder = new TemporaryDirectory();
        await using var context = await AppTestContext.CreateAsync();
        var main = context.CreateMain();
        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(folder.Path);

        var workspace = Assert.IsType<WorkspacePlaceholderViewModel>(main.CurrentPage);

        Assert.Equal(Path.GetFullPath(folder.Path), workspace.Result.Project.RootPath);
    }
}
