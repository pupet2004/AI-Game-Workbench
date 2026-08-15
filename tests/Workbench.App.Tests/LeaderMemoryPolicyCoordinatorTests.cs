using Workbench.Storage.Memory;
using Workbench.App.Memory;
using Workbench.App.Services;
using Workbench.Core.Memory;
using Workbench.Core.Projects;
using Workbench.Storage.Database;
using Workbench.Storage.Leaders;
using Workbench.Runtime.Registry;
using Workbench.App.Tests.Support;
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
    public async Task New_brain_does_not_run_a_second_model_pass_or_write_legacy_memory()
    {
        var runtime = new Support.FakeAgentRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        await using var context = await AppTestContext.CreateAsync(runtimeRegistry: registry);
        var workspace = await context.CreateWorkspaceForNewProjectAsync();
        await workspace.LeaderPane.InitializeAsync();
        runtime.QueueTurn(new Workbench.Runtime.Agents.AgentTurnCompleted(
            new Workbench.Runtime.Agents.AgentResult(Workbench.Runtime.Agents.AgentSessionId.New(), Workbench.Runtime.Agents.AgentSessionStatus.Completed, "old", null), context.Time.GetUtcNow()));
        workspace.LeaderPane.DraftMessage = "start";
        await workspace.LeaderPane.SendAsync();
        var candidate = await context.Services.ProjectMemoryService.CreateCandidateAsync(
            workspace.Result.Project.Id,
            "Legacy",
            "Retained memory",
            [new("Manual", "r4-04")]);
        var day = new DateOnly(2026, 8, 14);
        await context.Services.ProjectMemoryApi.UpsertDailySummaryAsync(
            new(workspace.Result.Project.Id, day, "Retained summary", null, [new("LeaderEpoch", workspace.LeaderPane.SessionEpochId!.Value.ToString())]),
            context.Time.GetUtcNow());
        var oldEpochId = workspace.LeaderPane.SessionEpochId;
        var before = await CountLegacyRowsAsync(context.Services.Database);
        var requestCount = runtime.SentRequests.Count;

        await workspace.LeaderPane.StartNewBrainAsync();

        Assert.NotEqual(oldEpochId, workspace.LeaderPane.SessionEpochId);
        Assert.Equal(requestCount, runtime.SentRequests.Count);
        Assert.Equal(before, await CountLegacyRowsAsync(context.Services.Database));
        Assert.Equal(candidate, await context.Services.ProjectMemoryService.GetMemoryItemAsync(candidate.Id));
        Assert.Equal("r4-04", Assert.Single(await context.Services.ProjectMemoryService.GetSourcesAsync(candidate.Id)).SourceRef);
        Assert.Equal("Retained summary", (await context.Services.ProjectMemoryApi.GetDailySummaryAsync(workspace.Result.Project.Id, day))!.Content);
        Assert.Null((await context.Services.LeaderSessionEpochRepository.GetAsync(oldEpochId!.Value))!.HandoffSummary);
    }

    [Fact]
    public async Task Restart_freezes_pending_jobs_and_preserves_legacy_readers_without_provider()
    {
        using var directory = new TemporaryDirectory("legacy-memory-restart");
        var databasePath = Path.Combine(directory.Path, "workbench.db");
        var projectRoot = Path.Combine(directory.Path, "project");
        Directory.CreateDirectory(projectRoot);
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-08-15T00:00:00+00:00"));
        var services = AppServices.CreateForDatabasePath(databasePath, time, new AgentRuntimeRegistry());
        await services.InitializeAsync();
        var project = (await services.ProjectOpenService.OpenAsync(projectRoot)).Project;
        var candidate = await services.ProjectMemoryService.CreateCandidateAsync(
            project.Id,
            "Legacy",
            "Retained after restart",
            [new("Manual", "restart-source")]);
        var day = new DateOnly(2026, 8, 15);
        await services.ProjectMemoryApi.UpsertDailySummaryAsync(
            new(project.Id, day, "Retained daily", null, [new("Manual", "daily-source")]),
            time.GetUtcNow());

        var first = CreateEpoch(project.Id, time.GetUtcNow(), "first");
        await services.ProjectLeaderRepository.CreateCurrentEpochAsync(
            new(project.Id, null, time.GetUtcNow(), time.GetUtcNow()), first);
        time.SetUtcNow(time.GetUtcNow().AddMinutes(1));
        var second = CreateEpoch(project.Id, time.GetUtcNow(), "second");
        await services.ProjectLeaderRepository.RolloverAsync(project.Id, first.Id, second, time.GetUtcNow(), "Test", null);
        await services.ProjectMemorySynthesisRepository.QueueSynthesisForEpochAsync(first.Id);
        time.SetUtcNow(time.GetUtcNow().AddMinutes(1));
        var third = CreateEpoch(project.Id, time.GetUtcNow(), "third");
        await services.ProjectLeaderRepository.RolloverAsync(project.Id, second.Id, third, time.GetUtcNow(), "Test", null);
        await services.ProjectMemorySynthesisRepository.QueueSynthesisForEpochAsync(second.Id);
        var running = await services.ProjectMemorySynthesisRepository.ClaimNextPendingAsync(project.Id);
        Assert.Equal(first.Id, running!.EpochId);
        var before = await CountLegacyRowsAsync(services.Database);
        await services.DisposeAsync();

        services = AppServices.CreateForDatabasePath(databasePath, time, new AgentRuntimeRegistry());
        try
        {
            await services.InitializeAsync();
            await services.InitializeAsync();
            services.ScheduleMemorySynthesis(project.Id);
            await Task.Delay(100);
            var reopenedFirst = await services.ProjectMemorySynthesisRepository.GetAsync(first.Id);
            var reopenedSecond = await services.ProjectMemorySynthesisRepository.GetAsync(second.Id);

            Assert.Equal(ProjectMemorySynthesisJobStatus.Running, reopenedFirst!.Status);
            Assert.Equal(1, reopenedFirst.AttemptCount);
            Assert.Equal(ProjectMemorySynthesisJobStatus.Pending, reopenedSecond!.Status);
            Assert.Equal(0, reopenedSecond.AttemptCount);
            Assert.Equal(before, await CountLegacyRowsAsync(services.Database));
            Assert.Equal(project.Id, (await services.ProjectOpenService.OpenAsync(projectRoot)).Project.Id);
            Assert.Equal(project.Id, (await services.ProjectOpenService.OpenAsync(projectRoot)).Project.Id);
            Assert.Equal("Retained after restart", (await services.ProjectMemoryService.GetMemoryItemAsync(candidate.Id))!.Content);
            Assert.Equal("restart-source", Assert.Single(await services.ProjectMemoryService.GetSourcesAsync(candidate.Id)).SourceRef);
            var daily = await services.ProjectMemoryApi.GetDailySummaryAsync(project.Id, day);
            Assert.Equal("Retained daily", daily!.Content);
            Assert.Equal("daily-source", Assert.Single(daily.Sources).SourceRef);
        }
        finally
        {
            await services.DisposeAsync();
        }
    }

    private static async Task<long[]> CountLegacyRowsAsync(WorkbenchDatabase database)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        string[] tables =
        [
            "project_activity_events",
            "project_memory_items",
            "project_memory_sources",
            "project_memory_synthesis_jobs",
            "project_daily_summaries",
            "project_daily_summary_sources"
        ];
        var counts = new long[tables.Length];
        for (var index = 0; index < tables.Length; index++)
        {
            var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {tables[index]};";
            counts[index] = Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        return counts;
    }

    private static StoredLeaderSessionEpoch CreateEpoch(Guid projectId, DateTimeOffset now, string suffix) =>
        new(
            Guid.NewGuid(),
            projectId,
            "offline-provider",
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "offline-model",
            Guid.NewGuid(),
            $"offline-{suffix}",
            "C:/Project",
            now,
            now,
            null,
            null,
            null);
}
