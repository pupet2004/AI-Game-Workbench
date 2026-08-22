using Workbench.App.Tests.Support;
using Workbench.App.Services;
using Workbench.App.ViewModels;
using Workbench.App.ViewModels.Panes;
using Workbench.Core.Projects;
using Workbench.Runtime.Registry;
using Workbench.Storage.Database;
using Workbench.Storage.Leaders;
using Workbench.Storage.Memory;
using Workbench.Storage.Projects;
using CoreProject = Workbench.Core.Projects.Project;

namespace Workbench.App.Tests;

public sealed class TruthGovernanceR5ACertificationTests
{
    private static readonly DateTimeOffset T0 =
        DateTimeOffset.Parse("2026-08-20T10:15:30.0000000+00:00");

    [Fact]
    public async Task Summary_query_is_project_local_after_wiring()
    {
        using var directory = new TemporaryDirectory("r5a-project-local");
        var database = new WorkbenchDatabase(Path.Combine(directory.Path, "workbench.db"));
        await database.InitializeAsync();
        var projects = new ProjectRepository(database);
        var projectA = new CoreProject(
            Guid.NewGuid(), "Project A", Path.Combine(directory.Path, "a"),
            ProjectType.Generic, null, T0, T0);
        var projectB = new CoreProject(
            Guid.NewGuid(), "Project B", Path.Combine(directory.Path, "b"),
            ProjectType.Generic, null, T0, T0);
        await projects.UpsertAsync(projectA);
        await projects.UpsertAsync(projectB);
        var summaries = new ProjectSummaryRepository(database);
        await summaries.AppendAsync(
            projectA.Id,
            Guid.NewGuid(),
            [new SummaryDelta(
                T0,
                SummaryDeltaKind.Decision,
                "PROJECT_A_SUMMARY_MUST_NOT_LEAK",
                [new SummarySourceRef("LeaderMessage", "message-a")])],
            T0.AddMinutes(1));

        var unfilteredProjectB = await summaries.QueryAsync(new SummaryQuery(projectB.Id, 20));
        var filteredProjectB = await summaries.QueryAsync(new SummaryQuery(
            projectB.Id,
            20,
            [SummaryDeltaKind.Decision],
            T0.AddMinutes(-1),
            T0.AddMinutes(1),
            "LeaderMessage",
            "message-a"));
        var filteredProjectA = await summaries.QueryAsync(new SummaryQuery(
            projectA.Id,
            20,
            [SummaryDeltaKind.Decision],
            T0.AddMinutes(-1),
            T0.AddMinutes(1),
            "LeaderMessage",
            "message-a"));

        Assert.Empty(unfilteredProjectB);
        Assert.Empty(filteredProjectB);
        var owned = Assert.Single(filteredProjectA);
        Assert.Equal(projectA.Id, owned.ProjectId);
        Assert.Equal("PROJECT_A_SUMMARY_MUST_NOT_LEAK", owned.Text);
    }

    [Fact]
    public async Task Durable_pending_result_recovery_does_not_change_boot_context()
    {
        using var directory = new TemporaryDirectory("r5a-recovery-boot");
        var registry = new AgentRuntimeRegistry();
        await using var services = AppServices.CreateForDatabasePath(
            Path.Combine(directory.Path, "workbench.db"),
            runtimeRegistry: registry);
        await services.Database.InitializeAsync();
        var project = new CoreProject(
            Guid.NewGuid(), "Recovery project", Path.Combine(directory.Path, "project"),
            ProjectType.Generic, null, T0, T0);
        await services.ProjectRepository.UpsertAsync(project);
        var epoch = new StoredLeaderSessionEpoch(
            Guid.NewGuid(), project.Id, "offline-provider", Guid.NewGuid(),
            "offline-model", Guid.NewGuid(), "offline-session", project.RootPath,
            T0, T0, null, null, null);
        await services.ProjectLeaderRepository.CreateCurrentEpochAsync(
            new StoredProjectLeader(project.Id, null, T0, T0),
            epoch);
        var day = new DateOnly(2026, 8, 20);
        await services.DailySummaryRepository.SaveAsync(
            new DailySummaryWrite(
                project.Id,
                day,
                "LEGACY_DAILY_CONTEXT_MARKER",
                null,
                [new DailySummarySourceReference("Manual", "task-5")]),
            T0);
        await new LeaderEpochContinuityRepository(services.Database).SaveAsync(
            project.Id,
            new LeaderEpochContinuityPlan(
                epoch.Id,
                4096,
                [new ContinuityMaterialSelection(
                    0,
                    ContinuityMaterialKind.DailySummary,
                    $"daily:{day:yyyy-MM-dd}",
                    2048)],
                T0));
        const string baselineSummaryMarker = "PENDING_BASELINE_SUMMARY_MUST_STAY_DORMANT";
        await SeedCanonicalSummaryAndQuerySentinelAsync(
            services.Database,
            services.ProjectSummaryRepository,
            project.Id,
            baselineSummaryMarker);
        var summariesBeforeRecovery = await ReadRawSummarySnapshotAsync(
            services.Database,
            project.Id);
        var resultId = Guid.NewGuid();
        const string summaryMarker = "RECOVERED_R5A_SUMMARY_MUST_NOT_ENTER_BOOT";
        var payload = $$"""
            [{"occurred_at":"2026-08-20T10:15:30.0000000+00:00","kind":"Decision","text":"{{summaryMarker}}","source_refs":[{"source_kind":"LeaderMessage","source_locator":"pending-1"}]}]
            """;
        var pendingMessage = await services.LeaderMessageRepository.AppendAsync(
            epoch.Id,
            "assistant",
            "Visible pending reply.",
            T0,
            metadata: new LeaderResultMetadata(resultId, payload));
        var bootBeforeRecovery = await services.LeaderBootContextBuilder.BuildAsync(project, "boot probe");

        await services.InitializeAsync();

        var summariesAfterRecovery = await ReadRawSummarySnapshotAsync(
            services.Database,
            project.Id);
        Assert.Equal(summariesBeforeRecovery.EntryCount + 1, summariesAfterRecovery.EntryCount);
        Assert.Equal(
            summariesBeforeRecovery.SourceRefCount + 1,
            summariesAfterRecovery.SourceRefCount);
        Assert.DoesNotContain(summaryMarker, summariesBeforeRecovery.Entries, StringComparison.Ordinal);
        Assert.Contains(summaryMarker, summariesAfterRecovery.Entries, StringComparison.Ordinal);
        Assert.Contains(resultId.ToString(), summariesAfterRecovery.Entries, StringComparison.Ordinal);
        Assert.Contains(baselineSummaryMarker, summariesAfterRecovery.Entries, StringComparison.Ordinal);
        Assert.Contains(SummaryQuerySentinelText, summariesAfterRecovery.Entries, StringComparison.Ordinal);
        Assert.Empty(await services.LeaderMessageRepository.GetPendingSummaryResultsAsync());
        var storedMessage = Assert.Single(
            await services.LeaderMessageRepository.GetAllAsync(epoch.Id),
            message => message.Id == pendingMessage.Id);
        Assert.NotNull(storedMessage.SummaryPersistedAt);
        var bootAfterRecovery = await services.LeaderBootContextBuilder.BuildAsync(project, "boot probe");
        Assert.Equal(bootBeforeRecovery.Text, bootAfterRecovery.Text);
        Assert.Contains("LEGACY_DAILY_CONTEXT_MARKER", bootAfterRecovery.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(baselineSummaryMarker, bootAfterRecovery.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(summaryMarker, bootAfterRecovery.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(SummaryQuerySentinelText, bootAfterRecovery.Text, StringComparison.Ordinal);
        Assert.Empty(registry.Runtimes);
    }

    [Fact]
    public async Task Completed_turn_does_not_create_synthesis_job_or_second_send()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        var synthesis = new ProjectMemorySynthesisRepository(context.Database, context.Time);
        var dailySummaries = new DailySummaryRepository(context.Database);
        await context.Leaders.CreateIfMissingAsync(
            new StoredProjectLeader(
                context.ProjectA.Id, null, context.T0.AddDays(-2), context.T0.AddDays(-2)));
        var completedEpoch = ArchivedEpoch(context, "completed", context.T0.AddDays(-2));
        await context.Epochs.SaveAsync(completedEpoch);
        await synthesis.QueueSynthesisForEpochAsync(completedEpoch.Id);
        var claimed = await synthesis.ClaimNextPendingAsync(context.ProjectA.Id);
        Assert.Equal(completedEpoch.Id, claimed!.EpochId);
        await context.Memories.ApplySynthesisAsync(new ProjectMemorySynthesisApplication(
            completedEpoch.Id,
            context.ProjectA.Id,
            [],
            [],
            context.T0.AddDays(-2).AddHours(2)));
        var pendingEpoch = ArchivedEpoch(context, "pending", context.T0.AddDays(-1));
        await context.Epochs.SaveAsync(pendingEpoch);
        await synthesis.QueueSynthesisForEpochAsync(pendingEpoch.Id);
        var memory = new ProjectMemoryItem(
            Guid.NewGuid(), context.ProjectA.Id, "Candidate", "Frozen candidate",
            "LEGACY_MEMORY_MUST_NOT_BE_PROMOTED", "Active", context.T0, context.T0, null);
        await context.Memories.AddAsync(
            memory,
            [new ProjectMemorySource("Manual", "task-5")]);
        var day = DateOnly.FromDateTime(context.T0.UtcDateTime);
        await dailySummaries.SaveAsync(
            new DailySummaryWrite(
                context.ProjectA.Id,
                day,
                "DAILY_SUMMARY_MUST_NOT_CHANGE",
                null,
                [new DailySummarySourceReference("Manual", "task-5")]),
            context.T0);
        var completedJobBefore = await synthesis.GetAsync(completedEpoch.Id);
        var pendingJobBefore = await synthesis.GetAsync(pendingEpoch.Id);
        var memoryBefore = await context.Memories.GetAsync(memory.Id);
        var memorySourcesBefore = await context.Memories.GetSourcesAsync(memory.Id);
        var dailyBefore = await dailySummaries.GetAsync(context.ProjectA.Id, day);
        var countsBefore = await ReadLegacyCountsAsync(context.Database);
        Assert.Equal(ProjectMemorySynthesisJobStatus.Completed, completedJobBefore!.Status);
        Assert.Equal(ProjectMemorySynthesisJobStatus.Pending, pendingJobBefore!.Status);

        var runtime = context.CreateRuntime();
        runtime.QueueTurn(context.Completed("""
            {"response":"Visible reply.","draft_proposal":null,"memory_commands":null,"summary_deltas":[{"occurred_at":"2026-08-20T10:15:30.0000000+00:00","kind":"Decision","text":"CANONICAL_SAME_TURN_SUMMARY","source_refs":[{"source_kind":"LeaderMessage","source_locator":"completed-turn"}]}]}
            """));
        var pane = context.CreatePane(runtime);
        await pane.InitializeAsync();
        pane.DraftMessage = "complete one normal turn";

        await pane.SendAsync();

        Assert.Single(runtime.SentRequests);
        var summary = Assert.Single(await context.ReadSummariesAsync(context.ProjectA.Id));
        Assert.Equal("CANONICAL_SAME_TURN_SUMMARY", summary.Text);
        Assert.Equal(completedJobBefore, await synthesis.GetAsync(completedEpoch.Id));
        Assert.Equal(pendingJobBefore, await synthesis.GetAsync(pendingEpoch.Id));
        Assert.Equal(memoryBefore, await context.Memories.GetAsync(memory.Id));
        Assert.Equal(memorySourcesBefore, await context.Memories.GetSourcesAsync(memory.Id));
        var dailyAfter = await dailySummaries.GetAsync(context.ProjectA.Id, day);
        Assert.NotNull(dailyBefore);
        Assert.NotNull(dailyAfter);
        Assert.Equal(dailyBefore.ProjectId, dailyAfter.ProjectId);
        Assert.Equal(dailyBefore.LocalDate, dailyAfter.LocalDate);
        Assert.Equal(dailyBefore.Content, dailyAfter.Content);
        Assert.Equal(dailyBefore.Revision, dailyAfter.Revision);
        Assert.Equal(dailyBefore.CreatedAt, dailyAfter.CreatedAt);
        Assert.Equal(dailyBefore.UpdatedAt, dailyAfter.UpdatedAt);
        Assert.Equal(dailyBefore.Sources, dailyAfter.Sources);
        Assert.Equal(countsBefore, await ReadLegacyCountsAsync(context.Database));
    }

    [Fact]
    public async Task Provider_unavailable_reopen_boot_and_resume_do_not_query_or_inject_summary()
    {
        using var databaseDirectory = new TemporaryDirectory("r5a-provider-unavailable");
        using var projectDirectory = new TemporaryDirectory("r5a-provider-project");
        var databasePath = Path.Combine(databaseDirectory.Path, "workbench.db");
        CoreProject project;
        SummaryRawSnapshot before;
        await using (var initial = AppServices.CreateForDatabasePath(
            databasePath,
            runtimeRegistry: new AgentRuntimeRegistry()))
        {
            await initial.InitializeAsync();
            project = (await initial.ProjectOpenService.OpenAsync(projectDirectory.Path)).Project;
            var epoch = new StoredLeaderSessionEpoch(
                Guid.NewGuid(), project.Id, "offline-provider", Guid.NewGuid(),
                "offline-model", Guid.NewGuid(), "offline-session", project.RootPath,
                T0, T0, null, null, null);
            await initial.ProjectLeaderRepository.CreateCurrentEpochAsync(
                new StoredProjectLeader(project.Id, null, T0, T0),
                epoch);
            var day = new DateOnly(2026, 8, 21);
            await initial.DailySummaryRepository.SaveAsync(
                new DailySummaryWrite(
                    project.Id,
                    day,
                    "REOPEN_SELECTED_DAILY_MARKER",
                    null,
                    [new DailySummarySourceReference("Manual", "reopen")]),
                T0);
            await new LeaderEpochContinuityRepository(initial.Database).SaveAsync(
                project.Id,
                new LeaderEpochContinuityPlan(
                    epoch.Id,
                    4096,
                    [new ContinuityMaterialSelection(
                        0,
                        ContinuityMaterialKind.DailySummary,
                        $"daily:{day:yyyy-MM-dd}",
                        2048)],
                    T0));
            await SeedCanonicalSummaryAndQuerySentinelAsync(
                initial.Database,
                initial.ProjectSummaryRepository,
                project.Id,
                "REOPEN_CANONICAL_SUMMARY_MUST_STAY_DORMANT");
            before = await ReadRawSummarySnapshotAsync(initial.Database, project.Id);
        }

        var emptyRegistry = new AgentRuntimeRegistry();
        var restarted = AppServices.CreateForDatabasePath(
            databasePath,
            runtimeRegistry: emptyRegistry);
        var main = new MainWindowViewModel(restarted, new TestFolderPickerService(null));
        try
        {
            await main.InitializeAsync();
            var home = Assert.IsType<HomeViewModel>(main.CurrentPage);

            await home.OpenRecentProjectAsync(Assert.Single(home.RecentProjects));

            var workspace = Assert.IsType<WorkspaceViewModel>(main.CurrentPage);
            Assert.Equal(project.Id, workspace.Result.Project.Id);
            Assert.False(workspace.LeaderPane.IsRuntimeAvailable);
            Assert.Empty(emptyRegistry.Runtimes);
            var boot = await restarted.LeaderBootContextBuilder.BuildAsync(project, "reopen boot probe");
            Assert.Contains("REOPEN_SELECTED_DAILY_MARKER", boot.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "REOPEN_CANONICAL_SUMMARY_MUST_STAY_DORMANT",
                boot.Text,
                StringComparison.Ordinal);
            Assert.DoesNotContain(SummaryQuerySentinelText, boot.Text, StringComparison.Ordinal);
            Assert.Equal(before, await ReadRawSummarySnapshotAsync(restarted.Database, project.Id));
        }
        finally
        {
            await main.DisposeAsync();
        }
    }

    [Fact]
    public async Task Session_rollover_does_not_query_inject_or_mutate_summary()
    {
        await using var context = await PersistentLeaderContext.CreateAsync();
        await SeedCanonicalSummaryAndQuerySentinelAsync(
            context.Database,
            context.Summaries,
            context.ProjectA.Id,
            "ROLLOVER_CANONICAL_SUMMARY_MUST_STAY_DORMANT");
        var before = await ReadRawSummarySnapshotAsync(context.Database, context.ProjectA.Id);
        var runtime = context.CreateRuntime();
        var registry = new AgentRuntimeRegistry();
        registry.Register(runtime);
        var pane = new LeaderPaneViewModel(
            context.ProjectA,
            registry,
            context.CreateManager(),
            () => Task.CompletedTask,
            bootContextBuilder: context.CreateBootBuilder(),
            projectSummaryRepository: context.Summaries);
        runtime.QueueTurn(context.Completed("old visible answer"));
        await pane.InitializeAsync();
        pane.DraftMessage = "old question";
        await pane.SendAsync();
        var oldEpoch = await context.Epochs.GetCurrentForProjectAsync(context.ProjectA.Id);
        context.Time.SetUtcNow(context.T1);
        runtime.QueueTurn(context.Completed("CURRENT FOCUS\nROLLOVER_HANDOFF_MARKER"));

        var rollover = await context.CreateRolloverService(registry).RolloverAsync(
            context.ProjectA,
            pane.Session!,
            oldEpoch!,
            false,
            "Manual");

        Assert.Equal(2, runtime.SentRequests.Count);
        Assert.All(runtime.SentRequests, request =>
        {
            Assert.DoesNotContain(
                "ROLLOVER_CANONICAL_SUMMARY_MUST_STAY_DORMANT",
                request.Text,
                StringComparison.Ordinal);
            Assert.DoesNotContain(SummaryQuerySentinelText, request.Text, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(
            "ROLLOVER_CANONICAL_SUMMARY_MUST_STAY_DORMANT",
            rollover.HandoffSummary,
            StringComparison.Ordinal);
        Assert.Equal(before, await ReadRawSummarySnapshotAsync(context.Database, context.ProjectA.Id));
        var newEpochBoot = await context.CreateBootBuilder().BuildAsync(
            context.ProjectA,
            "new epoch probe");
        Assert.Contains("ROLLOVER_HANDOFF_MARKER", newEpochBoot.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ROLLOVER_CANONICAL_SUMMARY_MUST_STAY_DORMANT",
            newEpochBoot.Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(SummaryQuerySentinelText, newEpochBoot.Text, StringComparison.Ordinal);
    }

    private const string SummaryQuerySentinelText = "SUMMARY_QUERY_SENTINEL_MUST_NEVER_BE_READ";

    private static async Task SeedCanonicalSummaryAndQuerySentinelAsync(
        WorkbenchDatabase database,
        ProjectSummaryRepository summaries,
        Guid projectId,
        string canonicalText)
    {
        await summaries.AppendAsync(
            projectId,
            Guid.NewGuid(),
            [new SummaryDelta(
                T0,
                SummaryDeltaKind.Decision,
                canonicalText,
                [new SummarySourceRef("Manual", "round-1")])],
            T0);
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO project_summary_entries (
                entry_id, project_id, occurred_at, created_at, kind, text, result_id, delta_ordinal)
            VALUES (
                'SUMMARY_QUERY_SENTINEL_NOT_A_GUID', $projectId,
                '9999-12-31T23:59:59.0000000+00:00',
                '9999-12-31T23:59:59.0000000+00:00',
                'Decision', $text, 'SUMMARY_QUERY_SENTINEL_RESULT_NOT_A_GUID', 0);
            """;
        command.Parameters.AddWithValue("$projectId", projectId.ToString());
        command.Parameters.AddWithValue("$text", SummaryQuerySentinelText);
        await command.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<FormatException>(() => summaries.QueryAsync(
            new SummaryQuery(projectId, 200)));
    }

    private static async Task<SummaryRawSnapshot> ReadRawSummarySnapshotAsync(
        WorkbenchDatabase database,
        Guid projectId)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var entries = connection.CreateCommand();
        entries.CommandText = """
            SELECT entry_id, occurred_at, created_at, kind, text, result_id, delta_ordinal
            FROM project_summary_entries
            WHERE project_id=$projectId
            ORDER BY entry_id;
            """;
        entries.Parameters.AddWithValue("$projectId", projectId.ToString());
        var entryRows = new List<string>();
        await using (var reader = await entries.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                entryRows.Add(string.Join('|',
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetString(5),
                    reader.GetInt64(6)));
            }
        }

        var refs = connection.CreateCommand();
        refs.CommandText = """
            SELECT ref.entry_id, ref.ordinal, ref.source_kind, ref.source_locator
            FROM project_summary_source_refs AS ref
            JOIN project_summary_entries AS entry ON entry.entry_id=ref.entry_id
            WHERE entry.project_id=$projectId
            ORDER BY ref.entry_id, ref.ordinal;
            """;
        refs.Parameters.AddWithValue("$projectId", projectId.ToString());
        var refRows = new List<string>();
        await using (var reader = await refs.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                refRows.Add(string.Join('|',
                    reader.GetString(0), reader.GetInt64(1),
                    reader.GetString(2), reader.GetString(3)));
            }
        }

        return new SummaryRawSnapshot(
            entryRows.Count,
            refRows.Count,
            string.Join('\n', entryRows),
            string.Join('\n', refRows));
    }

    private static StoredLeaderSessionEpoch ArchivedEpoch(
        PersistentLeaderContext context,
        string suffix,
        DateTimeOffset startedAt) =>
        new(
            Guid.NewGuid(),
            context.ProjectA.Id,
            "offline-provider",
            context.AccountId.Value,
            "offline-model",
            Guid.NewGuid(),
            $"offline-{suffix}",
            context.ProjectA.RootPath,
            startedAt,
            startedAt.AddHours(1),
            startedAt.AddHours(1),
            "seeded certification state",
            null);

    private static async Task<LegacyCounts> ReadLegacyCountsAsync(WorkbenchDatabase database)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM project_memory_items),
                (SELECT COUNT(*) FROM project_memory_sources),
                (SELECT COUNT(*) FROM project_memory_synthesis_jobs),
                (SELECT COUNT(*) FROM project_daily_summaries),
                (SELECT COUNT(*) FROM project_daily_summary_sources);
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new LegacyCounts(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4));
    }

    private sealed record LegacyCounts(
        long MemoryItems,
        long MemorySources,
        long SynthesisJobs,
        long DailySummaries,
        long DailySummarySources);

    private sealed record SummaryRawSnapshot(
        long EntryCount,
        long SourceRefCount,
        string Entries,
        string SourceRefs);
}
