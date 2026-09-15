using Workbench.App.Services;
using Workbench.Project.Git;
using Workbench.Runtime.Registry;

namespace Workbench.App.Tests;

public sealed class EnvironmentReadinessServiceTests
{
    private static readonly GitSnapshot ReadyGit =
        new(true, true, "C:\\Projects\\Counter", "abc", "main", false, false, null);

    [Fact]
    public void Ready_agent_and_non_godot_project_are_ready()
    {
        var resource = new WorkerResource("opencode", "account", "runtime", "OpenCode", "deepseek", "DeepSeek", true);
        var result = new EnvironmentReadinessService().Evaluate(
            ReadyGit, true, false, null, [resource], null);

        Assert.Equal(EnvironmentReadinessStatus.Ready, result.Git.Status);
        Assert.Equal(EnvironmentReadinessStatus.NotApplicable, result.Godot.Status);
        Assert.Equal(EnvironmentReadinessStatus.Ready, result.WorkbenchData.Status);
        Assert.Equal(EnvironmentReadinessStatus.Ready, result.Agent.Status);
        Assert.True(result.CanStart);
    }

    [Fact]
    public void Missing_git_and_godot_are_reported_without_fallback_repair()
    {
        var result = new EnvironmentReadinessService().Evaluate(
            new GitSnapshot(false, false, null, null, null, false, false, "Git executable was not found."),
            true, true, "C:\\missing\\godot.exe", [], "The local OpenCode CLI installation was not found.");

        Assert.Equal(EnvironmentReadinessStatus.NotInstalled, result.Git.Status);
        Assert.Equal(EnvironmentReadinessStatus.NotInstalled, result.Godot.Status);
        Assert.Equal(EnvironmentReadinessStatus.NotInstalled, result.Agent.Status);
        Assert.False(result.CanStart);
    }

    [Fact]
    public void Repository_without_git_history_is_installed_but_not_configured()
    {
        var result = new EnvironmentReadinessService().Evaluate(
            new GitSnapshot(true, false, null, null, null, false, false, null),
            true, false, null, [], null);

        Assert.Equal(EnvironmentReadinessStatus.InstalledButNotConfigured, result.Git.Status);
        Assert.Equal(EnvironmentReadinessStatus.AuthenticationRequired, result.Agent.Status);
    }
}
