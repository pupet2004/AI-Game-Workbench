using Workbench.App.ViewModels.Leader;
using Workbench.App.ViewModels.Panes;
using Workbench.Core.Projects;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Providers.Codex;
using Workbench.Runtime.Registry;
using Xunit.Abstractions;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class LiveLeaderIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Real_codex_completes_two_turn_project_leader_session()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("WORKBENCH_RUN_CODEX_INTEGRATION"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var executable = RequireEnvironmentVariable("WORKBENCH_CODEX_EXECUTABLE");
        var entryPoint = RequireEnvironmentVariable("WORKBENCH_CODEX_ENTRY");
        var projectRoot = RequireEnvironmentVariable("WORKBENCH_CODEX_CWD");
        var options = new CodexAppServerOptions(
            executable,
            [entryPoint, "app-server", "--stdio"],
            projectRoot,
            TimeSpan.FromMinutes(2));

        await using var runtime = await CodexAgentRuntime.ConnectAsync(options, ProviderAccountId.New());
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        var project = new CoreProject(
            Guid.NewGuid(),
            "Live Leader Smoke",
            projectRoot,
            ProjectType.Generic,
            projectRoot,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var pane = new LeaderPaneViewModel(
            project,
            registry,
            new ProjectLeaderSessionManager(),
            () => Task.CompletedTask);
        await pane.InitializeAsync();
        Assert.NotEmpty(pane.AvailableModels);
        pane.SelectedModel ??= pane.AvailableModels[0];

        pane.DraftMessage = "Read no files and run no commands. Reply with exactly: LEADER_UI_OK";
        await pane.SendAsync();
        var firstSession = Assert.IsType<AgentSession>(pane.Session);
        Assert.Contains(pane.Messages, message =>
            message.Role == LeaderMessageRole.Assistant &&
            message.Text.Contains("LEADER_UI_OK", StringComparison.Ordinal));

        pane.DraftMessage = "Reply with exactly: LEADER_SESSION_OK";
        await pane.SendAsync();
        var secondSession = Assert.IsType<AgentSession>(pane.Session);
        Assert.Contains(pane.Messages, message =>
            message.Role == LeaderMessageRole.Assistant &&
            message.Text.Contains("LEADER_SESSION_OK", StringComparison.Ordinal));

        Assert.Equal(firstSession.Id, secondSession.Id);
        Assert.Equal(firstSession.ExternalSessionId, secondSession.ExternalSessionId);
        output.WriteLine($"AgentSessionId: {secondSession.Id}");
        output.WriteLine($"ExternalSessionId: {secondSession.ExternalSessionId}");
    }

    private static string RequireEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Integration environment variable '{name}' is required.");
}
