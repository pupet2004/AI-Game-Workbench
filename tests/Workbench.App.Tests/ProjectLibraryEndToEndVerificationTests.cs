using Workbench.App.Leader;
using Workbench.App.ViewModels.Panes;
using Workbench.Storage.Memory;
using System.Text.Json;
using Workbench.App.Tests.Support;

namespace Workbench.App.Tests;

public sealed class ProjectLibraryEndToEndVerificationTests
{
    [Fact]
    public async Task Leader_history_is_written_accepted_and_browsable_through_all_library_axes()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("library-e2e-world");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var projectId = opened.Project.Id;

        var fixtures = new[]
        {
            ("World", "星陨谷", new DateOnly(2026, 8, 20), "星陨谷是一个被陨星雨改变生态的中世纪山谷。", "2026-08-20T09:15:00+00:00", "世界观建立。"),
            ("Rule", "陨星晶体", new DateOnly(2026, 8, 20), "陨星晶体只能在月蚀期间激活。", "2026-08-20T10:30:00+00:00", "确立陨星晶体激活规则。"),
            ("Boss", "灰烬领主", new DateOnly(2026, 8, 21), "灰烬领主居住在北部火山遗迹，能够操纵陨星尘。", "2026-08-21T14:20:00+00:00", "确立灰烬领主设定。")
        };

        Guid worldObjectId = Guid.Empty;
        foreach (var fixture in fixtures)
        {
            var proposal = await SubmitLeaderEnvelopeAsync(context, projectId, fixture, LibraryProposalAction.CreateNode, null, null, null, 0);
            if (fixture.Item1 == "World")
            {
                var createdObject = Assert.Single(await context.Services.ProjectLibraryEvolutionRepository.ListObjectsAsync(projectId));
                worldObjectId = createdObject.Id;
            }
        }

        var updateFixture = ("World", "星陨谷", new DateOnly(2026, 8, 22), "整体风格由传统中世纪骑士世界改为带有田园气息的陨星幻想世界。", "2026-08-22T16:45:00+00:00", "调整世界观整体风格。");
        await SubmitLeaderEnvelopeAsync(context, projectId, updateFixture, LibraryProposalAction.CreateNode, worldObjectId, null, null, 1);

        async Task<ProjectLibraryProposal> SubmitLeaderEnvelopeAsync(
            AppTestContext testContext,
            Guid id,
            (string, string, DateOnly, string, string, string) fixture,
            LibraryProposalAction action,
            Guid? targetObjectId,
            Guid? targetNodeId,
            int? expectedNodeRevision,
            int? expectedOverviewRevision)
        {
            var envelope = JsonSerializer.Serialize(new
            {
                response = "已整理项目资料。",
                draft_proposal = (object?)null,
                memory_commands = new
                {
                    daily_summary = (object?)null,
                    library_proposal = new
                    {
                        action = action.ToString(),
                        target_object_id = targetObjectId,
                        target_node_id = targetNodeId,
                        expected_node_revision = expectedNodeRevision,
                        expected_overview_revision = expectedOverviewRevision,
                        category = fixture.Item1,
                        topic = fixture.Item2,
                        local_date = fixture.Item3.ToString("yyyy-MM-dd"),
                        node_content = fixture.Item4,
                        current_overview = fixture.Item4,
                        materials = new[] { new { kind = "ScenarioNote", reference = $"world://{fixture.Item3:yyyy-MM-dd}/{fixture.Item1.ToLowerInvariant()}", label = "历史世界观笔记" } }
                    }
                },
                summary_deltas = new[]
                {
                    new
                    {
                        occurred_at = fixture.Item5,
                        kind = "Change",
                        text = fixture.Item6,
                        source_refs = new[] { new { source_kind = "LeaderMessage", source_locator = $"verification://{fixture.Item5}" } }
                    }
                }
            });

            Assert.True(LeaderStructuredResponse.TryParse(envelope, id, out var structured));
            var command = structured.MemoryCommands!.LibraryProposal!;
            var proposal = await context.Services.ProjectMemoryApi.CreateLibraryProposalAsync(
                new ProjectLibraryProposalDraft(
                    Guid.NewGuid(),
                    id,
                    Guid.NewGuid(),
                    command.Action,
                    command.TargetObjectId,
                    command.TargetNodeId,
                    command.ExpectedNodeRevision,
                    command.ExpectedOverviewRevision,
                    command.Category,
                    command.Topic,
                    command.LocalDate,
                    command.NodeContent,
                    command.CurrentOverview,
                    command.Materials,
                    DateTimeOffset.Parse(fixture.Item5)));

            if (await testContext.Services.ProjectMemoryApi.GetDailySummaryAsync(id, fixture.Item3) is null)
            {
                await context.Services.ProjectMemoryApi.UpsertDailySummaryAsync(
                    new(id, fixture.Item3, fixture.Item6, null, [new("LeaderMessage", $"verification://{fixture.Item5}")]),
                    DateTimeOffset.Parse(fixture.Item5));
            }
            await context.Services.ProjectSummaryRepository.AppendAsync(
                id,
                Guid.NewGuid(),
                structured.SummaryDeltas,
                DateTimeOffset.Parse(fixture.Item5));
            await context.Services.ProjectMemoryApi.AcceptLibraryProposalAsync(id, proposal.Id);
            return proposal;
        }

        var pane = new LibraryPaneViewModel(
            opened,
            () => Task.CompletedTask,
            evolutionLibrary: context.Services.ProjectLibraryEvolutionRepository,
            projectMemoryApi: context.Services.ProjectMemoryApi,
            projectSummaryRepository: context.Services.ProjectSummaryRepository);
        await pane.InitializeAsync();

        Assert.True(pane.HasOverviewView);
        Assert.Contains("World", pane.LibraryCategories);
        Assert.Contains("Rule", pane.LibraryCategories);
        Assert.Contains("Boss", pane.LibraryCategories);

        await pane.ShowCategoryAsync();
        await pane.SelectCategoryAsync("Boss");
        var boss = Assert.Single(pane.SelectedCategoryObjects);
        Assert.Equal("灰烬领主", boss.Topic);
        await pane.SelectLibraryObjectAsync(boss.Id);
        Assert.Contains("操纵陨星尘", Assert.Single(pane.ObjectTimeline).Content);
        Assert.Equal("world://2026-08-21/boss", Assert.Single(pane.ObjectTimeline.Single().Materials).Reference);

        await pane.ShowTimeAsync();
        Assert.Contains(2026, pane.TimeYears);
        await pane.SelectTimeYearAsync(2026);
        await pane.SelectTimeMonthAsync(8);
        await pane.SelectTimeDayAsync(new DateOnly(2026, 8, 22));
        var eventOnLastDay = Assert.Single(pane.TimeEvents, value => value.Category == "World");
        Assert.Contains("田园气息", eventOnLastDay.Text);
        var summaryEvent = Assert.Single(pane.TimeEvents, value => value.Category == "Summary");
        Assert.Equal("verification://2026-08-22T16:45:00+00:00", Assert.Single(summaryEvent.SummarySources).SourceLocator);
        Assert.Equal(new DateTimeOffset(2026, 8, 22, 16, 45, 0, TimeSpan.Zero), summaryEvent.OccurredAt);
        Assert.NotEqual(summaryEvent.OccurredAt, eventOnLastDay.OccurredAt);
    }
}
