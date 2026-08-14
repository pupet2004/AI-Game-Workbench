using Workbench.Storage.Memory;
using Workbench.App.Memory;
using Workbench.Core.Memory;
using Workbench.Core.Projects;
using Workbench.Storage.Database;
using Microsoft.Data.Sqlite;
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
    public async Task New_brain_preserves_explicit_daily_and_library_selection_without_enqueuing_legacy_synthesis()
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
        var libraryObject = await context.Services.ProjectLibraryEvolutionRepository.CreateObjectAsync(workspace.Result.Project.Id, "Design", "Relics", context.Time.GetUtcNow());
        libraryObject = await context.Services.ProjectLibraryEvolutionRepository.UpdateOverviewAsync(
            workspace.Result.Project.Id, libraryObject.Id, "SELECTED_LIBRARY_OVERVIEW", libraryObject.OverviewRevision, context.Time.GetUtcNow());
        var libraryNode = await context.Services.ProjectLibraryEvolutionRepository.AddNodeAsync(
            workspace.Result.Project.Id, libraryObject.Id, new DateOnly(2026, 8, 14), "SELECTED_LIBRARY_TIMELINE", [], context.Time.GetUtcNow());
        Assert.Empty(await context.Services.ProjectMemoryApi.GetPendingLibraryProposalsAsync(workspace.Result.Project.Id));
        var sourceEpoch = workspace.LeaderPane.SessionEpochId!.Value;
        await context.Services.ProjectMemoryApi.SaveBrainHandoffAsync(workspace.Result.Project.Id, sourceEpoch, "OLD_HANDOFF");
        await context.Services.LeaderMessageRepository.AppendAsync(sourceEpoch, "assistant", "SELECTED_RAW_MARKER", context.Time.GetUtcNow());

        var payload = """
            {"brain_handoff":"SELECTED_HANDOFF_MARKER","daily_summary":null,"total_continuity_budget_utf8_bytes":6000,"continuity_selection":[{"ordinal":0,"kind":"DailySummary","reference":"daily:2026-08-14","max_utf8_bytes":1000,"selector":null},{"ordinal":1,"kind":"LibraryOverview","reference":"library-overview:LIBRARY","max_utf8_bytes":1000,"selector":null},{"ordinal":2,"kind":"LibraryTimelineNode","reference":"library-timeline:NODE","max_utf8_bytes":1000,"selector":null},{"ordinal":3,"kind":"BrainHandoff","reference":"handoff:SOURCE","max_utf8_bytes":1000,"selector":null},{"ordinal":4,"kind":"RecentConversation","reference":"raw:SOURCE","max_utf8_bytes":1000,"selector":{"maxMessages":1,"maxUtf8Bytes":1000}}]}
            """.Replace("SOURCE", sourceEpoch.ToString(), StringComparison.Ordinal);
        payload = payload.Replace("LIBRARY", libraryObject.Id.ToString(), StringComparison.Ordinal)
            .Replace("NODE", libraryNode.Id.ToString(), StringComparison.Ordinal);
        var synthesisJobsBefore = await CountSynthesisJobsAsync(context.Services.Database);
        var legacyRowsBefore = await CountLegacyRowsAsync(context.Services.Database);
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
        Assert.Equal(["daily:2026-08-14", $"library-overview:{libraryObject.Id}", $"library-timeline:{libraryNode.Id}", $"handoff:{sourceEpoch}", $"raw:{sourceEpoch}"], plan!.Selections.Select(item => item.Reference));
        Assert.Equal("SELECTED_HANDOFF_MARKER", (await context.Services.LeaderSessionEpochRepository.GetAsync(sourceEpoch))!.HandoffSummary);
        Assert.Equal(synthesisJobsBefore, await CountSynthesisJobsAsync(context.Services.Database));
        Assert.Equal(legacyRowsBefore, await CountLegacyRowsAsync(context.Services.Database));

        runtime.SendException = new IOException("delivery failed");
        workspace.LeaderPane.DraftMessage = "failed delivery";
        await workspace.LeaderPane.SendAsync();
        Assert.Equal(plan.Selections, (await new LeaderEpochContinuityRepository(context.Services.Database)
            .GetAsync(workspace.Result.Project.Id, epochId))!.Selections);
        Assert.Null((await context.Services.LeaderSessionEpochRepository.GetAsync(epochId))!.BootContextDeliveredAt);
        Assert.Equal("SELECTED_HANDOFF_MARKER", (await context.Services.LeaderSessionEpochRepository.GetAsync(sourceEpoch))!.HandoffSummary);

        runtime.SendException = null;
        runtime.QueueTurn(new Workbench.Runtime.Agents.AgentTurnCompleted(
            new Workbench.Runtime.Agents.AgentResult(Workbench.Runtime.Agents.AgentSessionId.New(), Workbench.Runtime.Agents.AgentSessionStatus.Completed, "new", null), context.Time.GetUtcNow()));
        workspace.LeaderPane.DraftMessage = "continue";
        await workspace.LeaderPane.SendAsync();

        Assert.Contains("SELECTED_DAILY_MARKER", runtime.SentRequests.Last().Text, StringComparison.Ordinal);
        Assert.Contains("SELECTED_HANDOFF_MARKER", runtime.SentRequests.Last().Text, StringComparison.Ordinal);
        Assert.Contains("SELECTED_RAW_MARKER", runtime.SentRequests.Last().Text, StringComparison.Ordinal);
        Assert.Contains("SELECTED_LIBRARY_OVERVIEW", runtime.SentRequests.Last().Text, StringComparison.Ordinal);
        Assert.Contains("SELECTED_LIBRARY_TIMELINE", runtime.SentRequests.Last().Text, StringComparison.Ordinal);
        Assert.DoesNotContain("UNSELECTED_DAILY_MARKER", runtime.SentRequests.Last().Text, StringComparison.Ordinal);
        Assert.Contains("continue", runtime.SentRequests.Last().Text, StringComparison.Ordinal);
        Assert.Empty(await context.Services.DailySummaryRepository.ListAsync(workspace.Result.Project.Id, new DateOnly(2026, 8, 15), new DateOnly(2026, 8, 15)));
        Assert.Equal(synthesisJobsBefore, await CountSynthesisJobsAsync(context.Services.Database));
        Assert.Empty((await new LeaderEpochContinuityRepository(context.Services.Database)
            .GetAsync(workspace.Result.Project.Id, epochId))!.Selections);
        Assert.Equal("SELECTED_LIBRARY_OVERVIEW", (await context.Services.ProjectLibraryEvolutionRepository
            .GetObjectAsync(workspace.Result.Project.Id, libraryObject.Id))!.CurrentOverview);
        Assert.Null((await context.Services.LeaderSessionEpochRepository.GetAsync(sourceEpoch))!.HandoffSummary);
        Assert.Contains("SELECTED_RAW_MARKER", (await context.Services.LeaderMessageRepository.GetAllAsync(sourceEpoch)).Select(message => message.Text));
    }

    private static async Task<long> CountSynthesisJobsAsync(WorkbenchDatabase database)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM project_memory_synthesis_jobs;";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long[]> CountLegacyRowsAsync(WorkbenchDatabase database)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        string[] tables = ["project_activity_events", "project_memory_items", "project_memory_sources", "project_library_entries"];
        var counts = new long[tables.Length];
        for (var index = 0; index < tables.Length; index++)
        {
            var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {tables[index]};";
            counts[index] = Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        return counts;
    }
}
