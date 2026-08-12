using Workbench.App.Tests.Support;
using Workbench.App.ViewModels;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Registry;

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

    [Fact]
    public async Task Back_to_projects_preserves_leader_state_in_memory()
    {
        using var folder = new TemporaryDirectory();
        var runtime = new FakeAgentRuntime();
        var registry = RegistryWith(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var main = context.CreateMain();
        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(folder.Path);
        var workspace = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);
        QueueCompleted(runtime, "answer");
        workspace.LeaderPane.DraftMessage = "question";
        await workspace.LeaderPane.SendAsync();

        await main.BackToHomeAsync();

        Assert.Equal(1, context.LeaderSessions.Count);
        Assert.IsType<HomeViewModel>(main.CurrentPage);
    }

    [Fact]
    public async Task Reopening_project_restores_conversation()
    {
        using var folder = new TemporaryDirectory();
        var runtime = new FakeAgentRuntime();
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: RegistryWith(runtime));
        var main = context.CreateMain();
        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(folder.Path);
        var first = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);
        QueueCompleted(runtime, "answer");
        first.LeaderPane.DraftMessage = "question";
        await first.LeaderPane.SendAsync();
        await main.BackToHomeAsync();

        var home = Assert.IsType<HomeViewModel>(main.CurrentPage);
        await home.OpenRecentProjectAsync(Assert.Single(home.RecentProjects));
        var reopened = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);

        Assert.Equal(["question", "answer"], reopened.LeaderPane.Messages.Select(message => message.Text));
        Assert.Same(first.LeaderPane.Session, reopened.LeaderPane.Session);
    }

    [Fact]
    public async Task Opening_second_project_has_empty_independent_leader()
    {
        using var firstFolder = new TemporaryDirectory();
        using var secondFolder = new TemporaryDirectory();
        var runtime = new FakeAgentRuntime();
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: RegistryWith(runtime));
        var main = context.CreateMain();
        await main.InitializeAsync();
        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(firstFolder.Path);
        var first = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);
        QueueCompleted(runtime, "answer-a");
        first.LeaderPane.DraftMessage = "question-a";
        await first.LeaderPane.SendAsync();
        await main.BackToHomeAsync();

        await ((HomeViewModel)main.CurrentPage).OpenPathAsync(secondFolder.Path);
        var second = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);

        Assert.Empty(second.LeaderPane.Messages);
        Assert.Null(second.LeaderPane.Session);
        await main.BackToHomeAsync();
        var firstRecent = ((HomeViewModel)main.CurrentPage).RecentProjects.Single(item => item.RootPath == Path.GetFullPath(firstFolder.Path));
        await ((HomeViewModel)main.CurrentPage).OpenRecentProjectAsync(firstRecent);
        Assert.Equal(["question-a", "answer-a"], ((WorkspaceViewModel)main.CurrentPage).LeaderPane.Messages.Select(message => message.Text));
    }

    private static AgentRuntimeRegistry RegistryWith(FakeAgentRuntime runtime)
    {
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        return registry;
    }

    private static void QueueCompleted(FakeAgentRuntime runtime, string text)
    {
        var now = DateTimeOffset.UtcNow;
        runtime.QueueTurn(
            new AgentTextDelta(text, now),
            new AgentTurnCompleted(
                new AgentResult(AgentSessionId.New(), AgentSessionStatus.Completed, text, null),
                now));
    }
}
