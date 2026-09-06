using Workbench.App.Memory;
using Workbench.App.Leader;
using Workbench.Storage.Database;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;
using Workbench.Storage.Settings;
using System.Text;

namespace Workbench.App.Tests;

public sealed class ProjectContinuityMaterialServiceTests
{
    [Fact]
    public async Task Initial_boot_bundle_without_plan_includes_authority_library_and_latest_legacy_work()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var directory = new Support.TemporaryDirectory("initial-recovery");
        var project = (await context.Services.ProjectOpenService.OpenAsync(directory.Path)).Project;
        var now = context.Time.GetUtcNow();
        var projectRef = new Workbench.Core.Continuity.ProjectRef(project.Id);
        var principal = new Workbench.Core.Continuity.UserPrincipalRef(context.Services.UserPrincipalProvider.GetCurrent().Value);
        await context.Services.B1ProjectGovernance.CreateGovernedProjectForExistingProjectAsync(projectRef, principal);
        await context.Services.B1AuthorityCommands.AuthorAcceptedStateAsync(new Workbench.Core.Continuity.AuthorAcceptedStateCommand(
            projectRef, principal, new Workbench.Core.Continuity.DecidingAuthorityRef.UserPrincipal(principal), [],
            [
                new("从零刻开始，人类可以进行时间穿梭。", new Workbench.Core.Continuity.ContributionScopeTarget.Project(projectRef), null, null),
                new("世界只有一条闭合时间线，不存在平行宇宙。", new Workbench.Core.Continuity.ContributionScopeTarget.Project(projectRef), null, null)
            ]));
        var libraryObject = await context.Services.ProjectLibraryEvolutionRepository.CreateObjectAsync(project.Id, "Chapter Delivery", "Chapter 1 交付与未接受生活化设定", now);
        await context.Services.ProjectLibraryEvolutionRepository.AddNodeAsync(project.Id, libraryObject.Id, new DateOnly(2026, 9, 5), "Chapter 1 已完成；时间礼仪课仍是 Proposal / Recommendation。", [new("Artifact", "chapter-01.md", "Chapter 1 manuscript")], now);
        var taskId = Guid.NewGuid();
        var profile = Workbench.Core.Tasks.ExecutionProfile.Create("codex", "account", "model", "runtime");
        var revision = new Workbench.Core.Tasks.TaskRevision(taskId, 1, "Complete Chapter 1", "bounded", "none", ["done"], Workbench.Core.Tasks.TaskRiskLevel.Low, profile, "test", Workbench.Core.Tasks.TaskRevisionApprover.User, now, null);
        await new Workbench.Storage.Tasks.TaskRepository(context.Services.Database).CreateAsync(project.Id, new Workbench.Core.Tasks.TaskDraft(
            taskId, "Chapter 1 delivery", "Complete Chapter 1", "bounded", "none", ["done"], Workbench.Core.Tasks.TaskRiskLevel.Low, profile, now, revision, Workbench.Core.Tasks.TaskLifecycleStatus.Completed));
        var workerSessionId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var events = new Workbench.Storage.Workers.TaskEventRepository(context.Services.Database);
        await events.AppendAsync(new Workbench.Storage.Workers.StoredTaskEvent(reportId, project.Id, taskId, null, "WorkerFinalReportReceived",
            System.Text.Json.JsonSerializer.Serialize(new { WorkerSessionId = workerSessionId, Message = "Chapter 1 completed; chapter-01.md produced.", ValidationSummary = "green" }), now));
        await events.AppendAsync(new Workbench.Storage.Workers.StoredTaskEvent(Guid.NewGuid(), project.Id, taskId, null, "WorkerToLeaderHandoff",
            System.Text.Json.JsonSerializer.Serialize(new { ProjectId = project.Id, TaskId = taskId, WorkerSessionId = workerSessionId, WorkerLabel = "Worker", Status = Workbench.Runtime.Agents.AgentSessionStatus.Completed, Message = "Chapter 1 completed; chapter-01.md produced.", CreatedAt = now, Kind = Workbench.App.Worker.WorkerHandoffKind.FinalReport, ValidationSummary = "green", SourceEventId = reportId, TaskRevisionId = revision.Id }), now));

        var bundle = await context.Services.ProjectMemoryApi.BuildInitialContinuityBundleAsync(project.Id);

        Assert.Contains(bundle.Materials, item => item.Kind == ContinuityMaterialKind.AcceptedProjectState && item.Content.Contains("从零刻开始", StringComparison.Ordinal));
        Assert.Contains(bundle.Materials, item => item.Kind == ContinuityMaterialKind.LibraryTimelineNode && item.Content.Contains("时间礼仪课", StringComparison.Ordinal) && item.Content.Contains("chapter-01.md", StringComparison.Ordinal));
        Assert.Contains(bundle.Materials, item => item.Kind == ContinuityMaterialKind.LegacyWorkerCompletion && item.Content.Contains("Chapter 1 completed", StringComparison.Ordinal));
        Assert.Contains(bundle.Materials, item => item.Kind == ContinuityMaterialKind.AssignmentStatus && item.Content.Contains("Chapter 1 delivery", StringComparison.Ordinal));
        Assert.True(bundle.Utf8Bytes <= 24_000);

        var boot = LeaderBootContextBuilder.BuildSelected(project, bundle.Materials, "根据当前 Project World 继续。").Text;
        Assert.Contains("PERSISTED LIBRARY PROJECTION", boot, StringComparison.Ordinal);
        Assert.Contains("chapter-01.md", boot, StringComparison.Ordinal);
        Assert.Contains("时间礼仪课", boot, StringComparison.Ordinal);
        Assert.Contains("Workspace files are artifacts or source materials only", boot, StringComparison.Ordinal);
        Assert.Contains("do not report it as pending_confirmation", boot, StringComparison.Ordinal);
        Assert.Contains("Library Proposal/Recommendation content remains non-authoritative", boot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catalog_lists_library_structure_and_sizes_without_body_text_and_skips_empty_overview()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var directory = new Support.TemporaryDirectory();
        var project = (await context.Services.ProjectOpenService.OpenAsync(directory.Path)).Project;
        var now = context.Time.GetUtcNow();
        var epoch = new StoredLeaderSessionEpoch(Guid.NewGuid(), project.Id, "codex", Guid.NewGuid(), "model", Guid.NewGuid(), "thread", project.RootPath, now, now, null, null, null, null);
        await context.Services.ProjectLeaderRepository.CreateCurrentEpochAsync(new(project.Id, null, now, now), epoch);

        var documented = await context.Services.ProjectLibraryEvolutionRepository.CreateObjectAsync(project.Id, "Design", "Relics", now);
        documented = await context.Services.ProjectLibraryEvolutionRepository.UpdateOverviewAsync(
            project.Id, documented.Id, "OVERVIEW BODY MUST STAY OUT OF CATALOG", documented.OverviewRevision, now.AddMinutes(1));
        var empty = await context.Services.ProjectLibraryEvolutionRepository.CreateObjectAsync(project.Id, "Design", "Empty", now);
        await using (var connection = context.Services.Database.CreateConnection())
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "UPDATE project_library_objects SET current_overview='' WHERE id=$id;";
            command.Parameters.AddWithValue("$id", empty.Id.ToString());
            await command.ExecuteNonQueryAsync();
        }
        var first = await context.Services.ProjectLibraryEvolutionRepository.AddNodeAsync(
            project.Id, documented.Id, new DateOnly(2026, 8, 13), "FIRST NODE BODY MUST STAY OUT OF CATALOG", [], now.AddMinutes(2));
        var second = await context.Services.ProjectLibraryEvolutionRepository.AddNodeAsync(
            project.Id, empty.Id, new DateOnly(2026, 8, 14), "SECOND NODE BODY MUST STAY OUT OF CATALOG", [], now.AddMinutes(3));

        var catalog = await context.Services.ProjectMemoryApi.ListContinuityMaterialsAsync(project.Id, epoch.Id);

        var overview = Assert.Single(catalog.Materials, item => item.Kind == ContinuityMaterialKind.LibraryOverview);
        Assert.Equal($"library-overview:{documented.Id}", overview.Reference);
        Assert.Equal(Encoding.UTF8.GetByteCount(documented.CurrentOverview!), overview.Utf8Bytes);
        Assert.Contains("Design", overview.Label, StringComparison.Ordinal);
        Assert.Contains("Relics", overview.Label, StringComparison.Ordinal);
        Assert.Contains(documented.Id.ToString(), overview.Label, StringComparison.Ordinal);
        Assert.Contains(documented.OverviewRevision.ToString(), overview.Label, StringComparison.Ordinal);
        Assert.DoesNotContain("OVERVIEW BODY", overview.Label, StringComparison.Ordinal);
        Assert.DoesNotContain($"library-overview:{empty.Id}", catalog.Materials.Select(item => item.Reference));

        var timeline = catalog.Materials.Where(item => item.Kind == ContinuityMaterialKind.LibraryTimelineNode).ToArray();
        Assert.Equal(
            new[] { $"library-timeline:{first.Id}", $"library-timeline:{second.Id}" }.Order(),
            timeline.Select(item => item.Reference).Order());
        Assert.Equal(Encoding.UTF8.GetByteCount("FIRST NODE BODY MUST STAY OUT OF CATALOG"), timeline.Single(item => item.Reference.EndsWith(first.Id.ToString(), StringComparison.Ordinal)).Utf8Bytes);
        var firstDescriptor = timeline.Single(item => item.Reference.EndsWith(first.Id.ToString(), StringComparison.Ordinal));
        Assert.Contains("2026-08-13", firstDescriptor.Label, StringComparison.Ordinal);
        Assert.Equal(first.CreatedAt, firstDescriptor.OccurredAt);
        Assert.All(timeline, item => Assert.DoesNotContain("NODE BODY", item.Label, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Wrong_kind_reference_is_omitted_instead_of_reading_another_material_type()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var directory = new Support.TemporaryDirectory();
        var project = (await context.Services.ProjectOpenService.OpenAsync(directory.Path)).Project;
        var now = context.Time.GetUtcNow();
        await context.Services.ProjectMemoryApi.UpsertDailySummaryAsync(
            new(project.Id, new DateOnly(2026, 8, 14), "DAILY MUST NOT MASQUERADE AS LIBRARY", null, []), now);
        var reference = "daily:2026-08-14";
        var plan = new LeaderEpochContinuityPlan(Guid.NewGuid(), 1_000,
            [new(0, ContinuityMaterialKind.LibraryOverview, reference, 1_000)], now);

        var resolved = await context.Services.ProjectMemoryApi.ResolveContinuityAsync(project.Id, plan);

        Assert.Empty(resolved.Materials);
        Assert.Equal([reference], resolved.OmittedReferences);
    }

    [Fact]
    public async Task Explicit_library_reads_return_selected_content_and_reference_metadata_while_rejecting_stale_and_foreign_references()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var directoryA = new Support.TemporaryDirectory();
        using var directoryB = new Support.TemporaryDirectory();
        var projectA = (await context.Services.ProjectOpenService.OpenAsync(directoryA.Path)).Project;
        var projectB = (await context.Services.ProjectOpenService.OpenAsync(directoryB.Path)).Project;
        var now = context.Time.GetUtcNow();
        var objectA = await context.Services.ProjectLibraryEvolutionRepository.CreateObjectAsync(projectA.Id, "Implementation", "Save", now);
        objectA = await context.Services.ProjectLibraryEvolutionRepository.UpdateOverviewAsync(projectA.Id, objectA.Id, "CURRENT SAVE FACT", 0, now.AddMinutes(1));
        var nodeA = await context.Services.ProjectLibraryEvolutionRepository.AddNodeAsync(
            projectA.Id,
            objectA.Id,
            new DateOnly(2026, 8, 14),
            "SAVE EVOLUTION FACT",
            [new("File", "src/save.cs", "Save implementation"), new("GitCommit", "abc123", null)],
            now.AddMinutes(2));
        var objectB = await context.Services.ProjectLibraryEvolutionRepository.CreateObjectAsync(projectB.Id, "Secret", "Foreign", now);
        objectB = await context.Services.ProjectLibraryEvolutionRepository.UpdateOverviewAsync(projectB.Id, objectB.Id, "PROJECT B SECRET", 0, now.AddMinutes(1));
        var stale = Guid.NewGuid();
        var plan = new LeaderEpochContinuityPlan(Guid.NewGuid(), 20_000,
        [
            new(0, ContinuityMaterialKind.LibraryOverview, $"library-overview:{objectA.Id}", 5_000),
            new(1, ContinuityMaterialKind.LibraryTimelineNode, $"library-timeline:{nodeA.Id}", 5_000),
            new(2, ContinuityMaterialKind.LibraryOverview, $"library-overview:{stale}", 5_000),
            new(3, ContinuityMaterialKind.LibraryOverview, $"library-overview:{objectB.Id}", 5_000)
        ], now);

        var resolved = await context.Services.ProjectMemoryApi.ResolveContinuityAsync(projectA.Id, plan);

        Assert.Equal([ContinuityMaterialKind.LibraryOverview, ContinuityMaterialKind.LibraryTimelineNode], resolved.Materials.Select(item => item.Kind));
        Assert.Equal("CURRENT SAVE FACT", resolved.Materials[0].Content);
        Assert.Equal(
            "SAVE EVOLUTION FACT\n\nMATERIAL REFERENCES\n- File | src/save.cs | Save implementation\n- GitCommit | abc123",
            resolved.Materials[1].Content.Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.DoesNotContain("PROJECT B SECRET", resolved.Materials.Select(item => item.Content));
        Assert.Equal([$"library-overview:{stale}", $"library-overview:{objectB.Id}"], resolved.OmittedReferences);
    }

    [Fact]
    public async Task Restarted_first_send_uses_only_selected_daily_and_library_without_old_transcript_handoff_or_runtime()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var directory = new Support.TemporaryDirectory();
        var project = (await context.Services.ProjectOpenService.OpenAsync(directory.Path)).Project;
        var now = context.Time.GetUtcNow();
        var successor = new StoredLeaderSessionEpoch(
            Guid.NewGuid(), project.Id, "codex", Guid.NewGuid(), "model", Guid.NewGuid(), "new-brain-thread",
            project.RootPath, now, now, null, null, null, null);
        await context.Services.ProjectLeaderRepository.CreateCurrentEpochAsync(new(project.Id, null, now, now), successor);
        await context.Services.ProjectMemoryApi.UpsertDailySummaryAsync(
            new(project.Id, new DateOnly(2026, 8, 14), "DAILY CONTINUITY BODY", null, []), now);
        var libraryObject = await context.Services.ProjectLibraryEvolutionRepository.CreateObjectAsync(project.Id, "Design", "Relics", now);
        libraryObject = await context.Services.ProjectLibraryEvolutionRepository.UpdateOverviewAsync(
            project.Id, libraryObject.Id, "LIBRARY OVERVIEW BODY", 0, now.AddMinutes(1));
        var selectedNode = await context.Services.ProjectLibraryEvolutionRepository.AddNodeAsync(
            project.Id, libraryObject.Id, new DateOnly(2026, 8, 13), "SELECTED TIMELINE BODY",
            [new("GitCommit", "abc123", "Milestone")], now.AddMinutes(2));
        await context.Services.ProjectLibraryEvolutionRepository.AddNodeAsync(
            project.Id, libraryObject.Id, new DateOnly(2026, 8, 12), "UNSELECTED TIMELINE BODY", [], now.AddMinutes(3));
        var plan = new LeaderEpochContinuityPlan(successor.Id, 32_000,
        [
            new(0, ContinuityMaterialKind.DailySummary, "daily:2026-08-14", 8_000),
            new(1, ContinuityMaterialKind.LibraryOverview, $"library-overview:{libraryObject.Id}", 8_000),
            new(2, ContinuityMaterialKind.LibraryTimelineNode, $"library-timeline:{selectedNode.Id}", 8_000)
        ], now);
        await new LeaderEpochContinuityRepository(context.Services.Database).SaveAsync(project.Id, plan);
        var countsBefore = await ReadSideEffectCountsAsync(context.Services.Database);

        var reopenedDatabase = new WorkbenchDatabase(context.DatabasePath);
        await reopenedDatabase.InitializeAsync();
        var reopenedEpochs = new LeaderSessionEpochRepository(reopenedDatabase);
        var reopenedMessages = new LeaderMessageRepository(reopenedDatabase);
        var reopenedLibrary = new ProjectLibraryEvolutionRepository(reopenedDatabase);
        var reopenedApi = new ProjectMemoryApi(
            new DailySummaryRepository(reopenedDatabase),
            new ProjectMemoryPreferencesRepository(reopenedDatabase, context.Time),
            reopenedEpochs,
            reopenedMessages,
            new ProjectLibraryProposalService(reopenedDatabase, context.Time),
            reopenedLibrary);
        var reopenedPlans = new LeaderEpochContinuityRepository(reopenedDatabase);
        var persisted = await reopenedPlans.GetAsync(project.Id, successor.Id);
        var catalog = await reopenedApi.ListContinuityMaterialsAsync(project.Id, successor.Id);
        var resolved = await reopenedApi.ResolveContinuityAsync(project.Id, persisted!);
        var request = await new LeaderBootContextBuilder(reopenedApi, reopenedEpochs, reopenedPlans)
            .BuildAsync(project, "Continue without old session.");

        Assert.Equal(
            ["daily:2026-08-14", $"library-overview:{libraryObject.Id}", $"library-timeline:{selectedNode.Id}"],
            persisted!.Selections.Select(item => item.Reference));
        Assert.Equal([0, 1, 2], persisted.Selections.Select(item => item.Ordinal));
        Assert.Equal(0, catalog.Materials.Single(item => item.Kind == ContinuityMaterialKind.RecentConversation).Utf8Bytes);
        Assert.Equal(
            [ContinuityMaterialKind.DailySummary, ContinuityMaterialKind.LibraryOverview, ContinuityMaterialKind.LibraryTimelineNode],
            resolved.Materials.Select(item => item.Kind));
        var text = request.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
        AssertOrder(text, "DAILY CONTINUITY BODY", "LIBRARY OVERVIEW BODY", "SELECTED TIMELINE BODY");
        Assert.DoesNotContain("UNSELECTED TIMELINE BODY", text, StringComparison.Ordinal);
        Assert.DoesNotContain("RecentConversation", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PREVIOUS SESSION HANDOFF", text, StringComparison.Ordinal);
        Assert.EndsWith("CURRENT USER MESSAGE\nContinue without old session.", text, StringComparison.Ordinal);
        Assert.Empty(await reopenedMessages.GetAllAsync(successor.Id));
        Assert.Null((await reopenedEpochs.GetAsync(successor.Id))!.HandoffSummary);
        Assert.Equal(countsBefore, await ReadSideEffectCountsAsync(reopenedDatabase));
    }

    [Fact]
    public async Task Invalid_explicit_selection_is_reported_without_substituting_material()
    {
        await using var context = await AppTestContext.CreateAsync();
        var reference = $"raw:{Guid.NewGuid()}";
        var plan = new LeaderEpochContinuityPlan(Guid.NewGuid(), 1000,
            [new(0, ContinuityMaterialKind.RecentConversation, reference, 1000, "{")], context.Time.GetUtcNow());

        var resolved = await context.Services.ProjectMemoryApi.ResolveContinuityAsync(Guid.NewGuid(), plan);

        Assert.Empty(resolved.Materials);
        Assert.Equal([reference], resolved.OmittedReferences);
    }

    [Fact]
    public async Task Catalog_is_metadata_only_and_explicit_selection_reads_daily_handoff_and_raw_without_side_effects()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var directory = new Support.TemporaryDirectory();
        var project = (await context.Services.ProjectOpenService.OpenAsync(directory.Path)).Project;
        var now = context.Time.GetUtcNow();
        var epoch = new StoredLeaderSessionEpoch(Guid.NewGuid(), project.Id, "codex", Guid.NewGuid(), "model", Guid.NewGuid(), "thread", project.RootPath, now, now, null, null, null, null);
        await context.Services.ProjectLeaderRepository.CreateCurrentEpochAsync(new(project.Id, null, now, now), epoch);
        await context.Services.ProjectMemoryApi.UpsertDailySummaryAsync(new(project.Id, new DateOnly(2026, 8, 14), "DAILY BODY", null, []), now);
        await context.Services.ProjectMemoryApi.SaveBrainHandoffAsync(project.Id, epoch.Id, "HANDOFF BODY");
        await context.Services.LeaderMessageRepository.AppendAsync(epoch.Id, "user", "RAW EARLY", now);
        await context.Services.LeaderMessageRepository.AppendAsync(epoch.Id, "assistant", "RAW LATE", now);

        var catalog = await context.Services.ProjectMemoryApi.ListContinuityMaterialsAsync(project.Id, epoch.Id);
        var plan = new LeaderEpochContinuityPlan(epoch.Id, 1000, [
            new(0, ContinuityMaterialKind.DailySummary, "daily:2026-08-14", 1000),
            new(1, ContinuityMaterialKind.BrainHandoff, $"handoff:{epoch.Id}", 1000),
            new(2, ContinuityMaterialKind.RecentConversation, $"raw:{epoch.Id}", 1000, "{\"beforeSequence\":2,\"maxMessages\":1,\"maxUtf8Bytes\":1000}")], now);
        var resolved = await context.Services.ProjectMemoryApi.ResolveContinuityAsync(project.Id, plan);

        Assert.Equal(["daily:2026-08-14", $"handoff:{epoch.Id}", $"raw:{epoch.Id}"], catalog.Materials.Select(item => item.Reference));
        Assert.All(catalog.Materials, item => Assert.DoesNotContain("BODY", item.Label, StringComparison.Ordinal));
        Assert.Equal(10, catalog.Materials.Single(item => item.Reference == "daily:2026-08-14").Utf8Bytes);
        Assert.Equal(["DAILY BODY", "HANDOFF BODY", "RAW EARLY"], resolved.Materials.Select(item => item.Content));
        var invalid = await context.Services.ProjectMemoryApi.ResolveContinuityAsync(Guid.NewGuid(), plan);
        Assert.Empty(invalid.Materials);
        Assert.Equal(["daily:2026-08-14", $"handoff:{epoch.Id}", $"raw:{epoch.Id}"], invalid.OmittedReferences);
    }

    private static async Task<long[]> ReadSideEffectCountsAsync(WorkbenchDatabase database)
    {
        string[] queries =
        [
            "SELECT COUNT(*) FROM project_library_objects;",
            "SELECT COUNT(*) FROM project_library_timeline_nodes;",
            "SELECT COUNT(*) FROM project_library_material_refs;",
            "SELECT COUNT(*) FROM project_library_proposals;",
            "SELECT COUNT(*) FROM project_daily_summaries;",
            "SELECT COUNT(*) FROM project_daily_summary_sources;",
            "SELECT COUNT(*) FROM leader_messages;",
            "SELECT COUNT(*) FROM leader_session_epochs WHERE handoff_summary IS NOT NULL;",
            "SELECT COUNT(*) FROM project_memory_synthesis_jobs;"
        ];
        var counts = new long[queries.Length];
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        for (var index = 0; index < queries.Length; index++)
        {
            var command = connection.CreateCommand();
            command.CommandText = queries[index];
            counts[index] = Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        return counts;
    }

    private static void AssertOrder(string value, params string[] markers)
    {
        var previous = -1;
        foreach (var marker in markers)
        {
            var current = value.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(current > previous, $"Expected '{marker}' after the previous selected material.");
            previous = current;
        }
    }
}
