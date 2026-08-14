using Workbench.Storage.Memory;
using Workbench.App.Memory;
using Workbench.Core.Memory;
using Workbench.Core.Projects;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class LeaderMemoryPolicyCoordinatorTests
{
    [Fact]
    public void Policy_prompt_explains_explicit_library_overview_and_timeline_selection_for_older_projects()
    {
        var project = new CoreProject(Guid.NewGuid(), "Archive", "C:/Archive", ProjectType.Generic, null,
            DateTimeOffset.Parse("2026-08-14T00:00:00+00:00"), DateTimeOffset.Parse("2026-08-14T00:00:00+00:00"));
        var catalog = new ContinuityMaterialCatalog(project.Id, Guid.NewGuid(),
        [
            new("library-overview:1", ContinuityMaterialKind.LibraryOverview, project.Id, "Design / Relics", null, 100),
            new("library-timeline:2", ContinuityMaterialKind.LibraryTimelineNode, project.Id, "Design / Relics / 2026-08-14", null, 200)
        ]);
        var preferences = new ProjectMemoryPreferences(project.Id, LibraryGranularityMode.Balanced,
            ContinuityMode.Balanced, "UTC", null, DateTimeOffset.Parse("2026-08-14T00:00:00+00:00"));

        var prompt = LeaderMemoryPolicyPromptBuilder.Build(project, catalog, preferences).Text;

        Assert.Contains("Current Overview", prompt, StringComparison.Ordinal);
        Assert.Contains("current factual state", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Timeline Node", prompt, StringComparison.Ordinal);
        Assert.Contains("historical evolution", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Library + Daily", prompt, StringComparison.Ordinal);
        Assert.Contains("explicit", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("descriptor reference only", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task New_brain_persists_only_explicit_daily_selection_and_first_send_resolves_it()
    {
        var runtime = new Support.FakeAgentRuntime();
        var registry = new Workbench.Runtime.Registry.AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        await workspace.LeaderPane.InitializeAsync();
        runtime.QueueTurn(new Workbench.Runtime.Agents.AgentTurnCompleted(
            new Workbench.Runtime.Agents.AgentResult(Workbench.Runtime.Agents.AgentSessionId.New(), Workbench.Runtime.Agents.AgentSessionStatus.Completed, "old", null), context.Time.GetUtcNow()));
        workspace.LeaderPane.DraftMessage = "start";
        await workspace.LeaderPane.SendAsync();
        await context.Services.ProjectMemoryApi.UpsertDailySummaryAsync(new(workspace.Result.Project.Id, new DateOnly(2026, 8, 14), "SELECTED_DAILY_MARKER", null, []), context.Time.GetUtcNow());
        await context.Services.ProjectMemoryApi.UpsertDailySummaryAsync(new(workspace.Result.Project.Id, new DateOnly(2026, 8, 13), "UNSELECTED_DAILY_MARKER", null, []), context.Time.GetUtcNow());
        Assert.Empty(await context.Services.ProjectMemoryApi.GetPendingLibraryProposalsAsync(workspace.Result.Project.Id));
        var sourceEpoch = workspace.LeaderPane.SessionEpochId!.Value;
        await context.Services.ProjectMemoryApi.SaveBrainHandoffAsync(workspace.Result.Project.Id, sourceEpoch, "OLD_HANDOFF");
        await context.Services.LeaderMessageRepository.AppendAsync(sourceEpoch, "assistant", "SELECTED_RAW_MARKER", context.Time.GetUtcNow());

        var payload = """
            {"brain_handoff":"SELECTED_HANDOFF_MARKER","daily_summary":null,"total_continuity_budget_utf8_bytes":4000,"continuity_selection":[{"ordinal":0,"kind":"DailySummary","reference":"daily:2026-08-14","max_utf8_bytes":1000,"selector":null},{"ordinal":1,"kind":"BrainHandoff","reference":"handoff:SOURCE","max_utf8_bytes":1000,"selector":null},{"ordinal":2,"kind":"RecentConversation","reference":"raw:SOURCE","max_utf8_bytes":1000,"selector":{"maxMessages":1,"maxUtf8Bytes":1000}}]}
            """.Replace("SOURCE", sourceEpoch.ToString(), StringComparison.Ordinal);
        runtime.QueueTurn(new Workbench.Runtime.Agents.AgentTurnCompleted(
            new Workbench.Runtime.Agents.AgentResult(Workbench.Runtime.Agents.AgentSessionId.New(), Workbench.Runtime.Agents.AgentSessionStatus.Completed, payload, null), context.Time.GetUtcNow()));
        var preparation = await context.Services.LeaderMemoryPolicyCoordinator.PrepareForNewBrainAsync(
            workspace.Result.Project,
            workspace.LeaderPane.Session!,
            (await context.Services.LeaderSessionEpochRepository.GetCurrentForProjectAsync(workspace.Result.Project.Id))!,
            false);
        Assert.True(preparation.Available, preparation.Error);
        runtime.QueueTurn(new Workbench.Runtime.Agents.AgentTurnCompleted(
            new Workbench.Runtime.Agents.AgentResult(Workbench.Runtime.Agents.AgentSessionId.New(), Workbench.Runtime.Agents.AgentSessionStatus.Completed, payload, null), context.Time.GetUtcNow()));
        await workspace.LeaderPane.StartNewBrainAsync();
        Assert.Empty(await context.Services.ProjectMemoryApi.GetPendingLibraryProposalsAsync(workspace.Result.Project.Id));
        var epochId = workspace.LeaderPane.SessionEpochId!.Value;
        var plan = await new LeaderEpochContinuityRepository(context.Services.Database).GetAsync(workspace.Result.Project.Id, epochId);
        Assert.Equal(["daily:2026-08-14", $"handoff:{sourceEpoch}", $"raw:{sourceEpoch}"], plan!.Selections.Select(item => item.Reference));
        Assert.Equal("SELECTED_HANDOFF_MARKER", (await context.Services.LeaderSessionEpochRepository.GetAsync(sourceEpoch))!.HandoffSummary);

        runtime.QueueTurn(new Workbench.Runtime.Agents.AgentTurnCompleted(
            new Workbench.Runtime.Agents.AgentResult(Workbench.Runtime.Agents.AgentSessionId.New(), Workbench.Runtime.Agents.AgentSessionStatus.Completed, "new", null), context.Time.GetUtcNow()));
        workspace.LeaderPane.DraftMessage = "continue";
        await workspace.LeaderPane.SendAsync();

        Assert.Contains("SELECTED_DAILY_MARKER", runtime.SentRequests.Last().Text, StringComparison.Ordinal);
        Assert.Contains("SELECTED_HANDOFF_MARKER", runtime.SentRequests.Last().Text, StringComparison.Ordinal);
        Assert.Contains("SELECTED_RAW_MARKER", runtime.SentRequests.Last().Text, StringComparison.Ordinal);
        Assert.DoesNotContain("UNSELECTED_DAILY_MARKER", runtime.SentRequests.Last().Text, StringComparison.Ordinal);
        Assert.Contains("continue", runtime.SentRequests.Last().Text, StringComparison.Ordinal);
        Assert.Empty(await context.Services.DailySummaryRepository.ListAsync(workspace.Result.Project.Id, new DateOnly(2026, 8, 15), new DateOnly(2026, 8, 15)));
    }
}
