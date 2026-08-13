using Workbench.App.ViewModels.Leader;
using Workbench.App.ViewModels.Panes;
using Workbench.App.Leader;
using Workbench.App.Services;
using Workbench.Core.Projects;
using Workbench.Runtime.Agents;
using Workbench.Runtime.Providers;
using Workbench.Runtime.Providers.Codex;
using Workbench.Runtime.Registry;
using Workbench.Storage.Memory;
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

    [Fact]
    public async Task Real_codex_fresh_epoch_answers_from_certified_project_memory_only()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("WORKBENCH_RUN_M105C_SMOKE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var projectId = Guid.Parse("105b0000-0000-4000-8000-000000000001");
        await using var services = AppServices.CreateDefault(CodexRuntimeComposition.ConnectAsync);
        await services.InitializeAsync();
        await services.RetryRuntimeAsync();
        var project = await services.ProjectRepository.GetByIdAsync(projectId)
            ?? throw new InvalidOperationException("The safe M1.5 smoke project was not found.");
        Assert.Equal("AI Game Workbench M105B Smoke", project.Name);

        var formal = (await services.ProjectMemoryService.GetFormalMemoriesAsync(project.Id))
            .SingleOrDefault(item => string.Equals(item.Topic, "Memory Boot Smoke", StringComparison.Ordinal));
        if (formal is null)
        {
            var candidate = await services.ProjectMemoryService.CreateCandidateAsync(
                project.Id,
                "Memory Boot Smoke",
                "The certified project marker is MEMORY_BOOT_FORMAL_731.",
                [new ProjectMemorySource("Manual", "M1.5C real Codex smoke")]);
            formal = await services.ProjectMemoryService.AcceptCandidateAsync(candidate.Id);
        }

        Assert.Equal("The certified project marker is MEMORY_BOOT_FORMAL_731.", formal.Content);
        var sessions = new ProjectLeaderSessionManager(
            services.ProjectLeaderRepository,
            services.LeaderSessionEpochRepository,
            services.LeaderMessageRepository,
            services.TimeProvider);
        var pane = new LeaderPaneViewModel(
            project,
            services.RuntimeRegistry,
            sessions,
            () => Task.CompletedTask,
            rolloverService: services.LeaderSessionRolloverService,
            epochRepository: services.LeaderSessionEpochRepository,
            messageRepository: services.LeaderMessageRepository,
            bootContextBuilder: services.LeaderBootContextBuilder);
        await pane.InitializeAsync();
        var oldEpochId = pane.SessionEpochId ?? throw new InvalidOperationException("The safe smoke project has no current epoch.");
        var oldExternalSessionId = pane.Session?.ExternalSessionId
            ?? throw new InvalidOperationException("The safe smoke project has no resumable runtime session.");

        await pane.StartNewBrainAsync();

        var newEpochId = pane.SessionEpochId ?? throw new InvalidOperationException("Manual New Brain created no epoch.");
        var newExternalSessionId = pane.Session?.ExternalSessionId
            ?? throw new InvalidOperationException("Manual New Brain created no external session.");
        Assert.NotEqual(oldEpochId, newEpochId);
        Assert.NotEqual(oldExternalSessionId, newExternalSessionId);
        Assert.Empty(await services.LeaderMessageRepository.GetAllAsync(newEpochId));

        const string userText = """
            Read no files and run no commands.
            Using only the Workbench project context,
            reply with exactly the certified project marker.
            """;
        pane.DraftMessage = userText;
        await pane.SendAsync();

        var assistant = Assert.Single(
            pane.Messages,
            message => message.Role == LeaderMessageRole.Assistant);
        Assert.Equal("MEMORY_BOOT_FORMAL_731", assistant.Text.Trim());
        var persisted = await services.LeaderMessageRepository.GetAllAsync(newEpochId);
        var persistedUser = Assert.Single(persisted, message => message.Role == "user");
        Assert.Equal(userText, persistedUser.Text);
        Assert.DoesNotContain("WORKBENCH PROJECT CONTEXT", persistedUser.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("MEMORY_BOOT_FORMAL_731", persistedUser.Text, StringComparison.Ordinal);
        Assert.NotNull((await services.LeaderSessionEpochRepository.GetAsync(newEpochId))!.BootContextDeliveredAt);
        Assert.Equal(
            ProjectMemorySynthesisJobStatus.Pending,
            (await services.ProjectMemorySynthesisRepository.GetAsync(oldEpochId))!.Status);

        output.WriteLine($"Old ExternalSessionId: {oldExternalSessionId}");
        output.WriteLine($"New ExternalSessionId: {newExternalSessionId}");
        output.WriteLine($"Assistant: {assistant.Text.Trim()}");
    }

    private static string RequireEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Integration environment variable '{name}' is required.");
}
