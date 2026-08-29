using Workbench.App.ViewModels.Panes;
using Workbench.App.Tests.Support;
using Workbench.Storage.Memory;
using Workbench.Storage.Continuity;
using Workbench.Core.Continuity;
using Workbench.App.Memory;

namespace Workbench.App.Tests;

public sealed class LibraryCategoryTimeViewTests
{
    [Fact]
    public async Task Category_and_time_are_symmetric_projections_of_same_library_nodes()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory();
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var library = new ProjectLibraryEvolutionRepository(context.Services.Database);
        var relics = await library.CreateObjectAsync(opened.Project.Id, "Design", "Relics", context.Time.GetUtcNow());
        await library.UpdateOverviewAsync(opened.Project.Id, relics.Id, "CURRENT_OVERVIEW", 0, context.Time.GetUtcNow());
        var old = await library.AddNodeAsync(opened.Project.Id, relics.Id, new DateOnly(2026, 8, 13), "OLD_NODE", [], context.Time.GetUtcNow());
        var newest = await library.AddNodeAsync(opened.Project.Id, relics.Id, new DateOnly(2026, 8, 14), "NEW_NODE", [new("GitCommit", "abc123", "Implementation")], context.Time.GetUtcNow());
        var sameDay = await library.AddNodeAsync(opened.Project.Id, relics.Id, new DateOnly(2026, 8, 14), "SAME_DAY_NODE", [], context.Time.GetUtcNow().AddMinutes(1));

        var pane = new LibraryPaneViewModel(opened, () => Task.CompletedTask, evolutionLibrary: library);
        await pane.InitializeAsync();
        await pane.ShowCategoryAsync();
        await pane.SelectLibraryObjectAsync(relics.Id);

        Assert.True(pane.HasCategoryView);
        Assert.Equal("CURRENT_OVERVIEW", pane.SelectedLibraryObject!.CurrentOverview);
        Assert.Equal(["SAME_DAY_NODE", "NEW_NODE", "OLD_NODE"], pane.ObjectTimeline.Select(node => node.Content));
        Assert.Equal(LibraryTimelineDirection.OldToNew, pane.TimelineDirection);
        Assert.Contains(pane.ObjectTimeline.Single(node => node.Id == newest.Id).Materials, item => item.Label == "Implementation");

        await pane.ShowTimeAsync();
        Assert.True(pane.HasTimeView);
        Assert.Equal([new DateOnly(2026, 8, 14), new DateOnly(2026, 8, 13)], pane.TimeDates);
        Assert.Equal([sameDay.Id, newest.Id], pane.TimeGroups.Single(group => group.LocalDate == new DateOnly(2026, 8, 14) && group.Category == "Design").Nodes.Select(node => node.Id));
        Assert.Equal(old.Id, pane.ObjectTimeline.Last().Id);
    }

    [Fact]
    public async Task Browsing_is_project_isolated_and_does_not_write_continuity_material()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var firstFolder = new TemporaryDirectory("library-first");
        using var secondFolder = new TemporaryDirectory("library-second");
        var firstProject = await context.Services.ProjectOpenService.OpenAsync(firstFolder.Path);
        var secondProject = await context.Services.ProjectOpenService.OpenAsync(secondFolder.Path);
        var library = context.Services.ProjectLibraryEvolutionRepository;
        var firstObject = await library.CreateObjectAsync(firstProject.Project.Id, "Design", "First", context.Time.GetUtcNow());
        await library.AddNodeAsync(firstProject.Project.Id, firstObject.Id, new DateOnly(2026, 8, 14), "FIRST_NODE", [], context.Time.GetUtcNow());
        var secondObject = await library.CreateObjectAsync(secondProject.Project.Id, "Design", "Second", context.Time.GetUtcNow());
        await library.AddNodeAsync(secondProject.Project.Id, secondObject.Id, new DateOnly(2026, 8, 15), "SECOND_NODE", [], context.Time.GetUtcNow());

        var pane = new LibraryPaneViewModel(firstProject, () => Task.CompletedTask, evolutionLibrary: library);
        await pane.InitializeAsync();
        await pane.ShowCategoryAsync();
        await pane.SelectLibraryObjectAsync(firstObject.Id);
        await pane.ShowTimeAsync();

        Assert.Equal([firstObject.Id], pane.LibraryObjects.Select(value => value.Id));
        Assert.Equal(["FIRST_NODE"], pane.TimeGroups.SelectMany(group => group.Nodes).Select(node => node.Content));
        Assert.Null(await context.Services.DailySummaryRepository.GetAsync(firstProject.Project.Id, new DateOnly(2026, 8, 14)));
        var synthesis = await context.Services.ProjectMemorySynthesisRepository.GetStatusAsync(firstProject.Project.Id);
        Assert.Equal(0, synthesis.PendingCount + synthesis.RunningCount);
    }

    [Fact]
    public async Task Category_detail_shows_a_bounded_empty_current_overview()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("library-empty-overview");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var libraryObject = await context.Services.ProjectLibraryEvolutionRepository.CreateObjectAsync(
            opened.Project.Id, "Design", "Unwritten", context.Time.GetUtcNow());

        var pane = new LibraryPaneViewModel(opened, () => Task.CompletedTask, evolutionLibrary: context.Services.ProjectLibraryEvolutionRepository);
        await pane.ShowCategoryAsync();
        Assert.Contains(pane.LibraryObjects, value => value.Id == libraryObject.Id);
        await pane.SelectLibraryObjectAsync(libraryObject.Id);

        Assert.Equal("No current overview yet.", pane.CurrentOverviewText);
        Assert.Empty(pane.ObjectTimeline);
    }

    [Fact]
    public async Task Selecting_the_current_library_object_again_collapses_its_detail()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("library-toggle-detail");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var library = context.Services.ProjectLibraryEvolutionRepository;
        var libraryObject = await library.CreateObjectAsync(opened.Project.Id, "Project", "Current status", context.Time.GetUtcNow());
        await library.UpdateOverviewAsync(opened.Project.Id, libraryObject.Id, "A long current overview.", 0, context.Time.GetUtcNow());

        var pane = new LibraryPaneViewModel(opened, () => Task.CompletedTask, evolutionLibrary: library);
        await pane.ShowCategoryAsync();
        await pane.SelectLibraryObjectAsync(libraryObject.Id);
        Assert.NotNull(pane.SelectedLibraryObject);

        await pane.SelectLibraryObjectAsync(libraryObject.Id);

        Assert.Null(pane.SelectedLibraryObject);
        Assert.Empty(pane.ObjectTimeline);
    }

    [Fact]
    public async Task Selecting_a_date_exposes_daily_summary_decisions_and_related_library_nodes()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("library-day-detail");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var library = context.Services.ProjectLibraryEvolutionRepository;
        var objectRef = await library.CreateObjectAsync(opened.Project.Id, "Boss", "Phase two", context.Time.GetUtcNow());
        var day = new DateOnly(2026, 8, 28);
        var occurredAt = new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        await library.AddNodeAsync(opened.Project.Id, objectRef.Id, day, "Added a transition attack.", [], context.Time.GetUtcNow());
        await context.Services.ProjectMemoryApi.UpsertDailySummaryAsync(
            new(opened.Project.Id, day, "Finished the second phase design.", null, []),
            context.Time.GetUtcNow());
        await context.Services.ProjectSummaryRepository.AppendAsync(
            opened.Project.Id,
            Guid.NewGuid(),
            [new(occurredAt, SummaryDeltaKind.Decision, "Keep the transition attack.", [])],
            context.Time.GetUtcNow());

        var pane = new LibraryPaneViewModel(
            opened,
            () => Task.CompletedTask,
            evolutionLibrary: library,
            projectMemoryApi: context.Services.ProjectMemoryApi,
            projectSummaryRepository: context.Services.ProjectSummaryRepository);
        await pane.InitializeAsync();
        await pane.ShowTimeAsync();
        await pane.SelectTimeDateAsync(day);

        Assert.NotNull(pane.SelectedTimeDay);
        Assert.Equal("Finished the second phase design.", pane.SelectedTimeDay!.Summary!.Content);
        Assert.Equal([SummaryDeltaKind.Decision], pane.SelectedTimeDay.SummaryEntries.Select(entry => entry.Kind));
        Assert.Equal(["Added a transition attack."], pane.SelectedTimeDay.Groups.SelectMany(group => group.Nodes).Select(node => node.Content));

        await pane.SelectTimeDateAsync(day);
        Assert.Null(pane.SelectedTimeDay);
    }

    [Fact]
    public async Task Selecting_a_category_exposes_only_objects_in_that_category()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("library-category-detail");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var library = context.Services.ProjectLibraryEvolutionRepository;
        var boss = await library.CreateObjectAsync(opened.Project.Id, "Boss", "Phase two", context.Time.GetUtcNow());
        var combat = await library.CreateObjectAsync(opened.Project.Id, "Combat", "Damage", context.Time.GetUtcNow());

        var pane = new LibraryPaneViewModel(opened, () => Task.CompletedTask, evolutionLibrary: library);
        await pane.InitializeAsync();
        await pane.SelectCategoryAsync("Boss");

        Assert.Equal("Boss", pane.SelectedCategory);
        Assert.Equal([boss.Id], pane.SelectedCategoryObjects.Select(item => item.Id));

        await pane.SelectCategoryAsync("Boss");
        Assert.Null(pane.SelectedCategory);
        Assert.Empty(pane.SelectedCategoryObjects);
        Assert.Contains(pane.LibraryObjects, item => item.Id == combat.Id);
    }

    [Fact]
    public async Task Legacy_and_b1_projection_nodes_keep_distinct_context_labels()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("library-context-labels");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        var library = context.Services.ProjectLibraryEvolutionRepository;
        var libraryObject = await library.CreateObjectAsync(
            opened.Project.Id,
            "Design",
            "Combat",
            context.Time.GetUtcNow());

        await library.AddNodeAsync(
            opened.Project.Id,
            libraryObject.Id,
            new DateOnly(2026, 8, 13),
            "Imported legacy entry.",
            [new("AgentSession", Guid.NewGuid().ToString(), "Legacy source session")],
            context.Time.GetUtcNow());
        await library.AddNodeAsync(
            opened.Project.Id,
            libraryObject.Id,
            new DateOnly(2026, 8, 14),
            "Explicit B1 projection.",
            [
                new(LibraryProjectionMaterialKinds.AuthorityDecision, Guid.NewGuid().ToString(), "Decision"),
                new(LibraryProjectionMaterialKinds.AcceptedContribution, Guid.NewGuid().ToString(), "Accepted contribution")
            ],
            context.Time.GetUtcNow().AddMinutes(1));

        var pane = new LibraryPaneViewModel(
            opened,
            () => Task.CompletedTask,
            evolutionLibrary: library);
        await pane.InitializeAsync();
        await pane.ShowCategoryAsync();
        await pane.SelectLibraryObjectAsync(libraryObject.Id);

        Assert.Equal(
            [LibraryTimelineContextKind.B1AcceptedProjection, LibraryTimelineContextKind.LegacyContext],
            pane.ObjectTimeline.Select(value => value.ContextKind));
        Assert.Equal("B1 ACCEPTED PROJECTION", pane.ObjectTimeline[0].ContextLabel);
        Assert.Equal("LEGACY CONTEXT", pane.ObjectTimeline[1].ContextLabel);
    }

    [Fact]
    public async Task Browsing_legacy_library_does_not_create_b1_effects()
    {
        await using var context = await AppTestContext.CreateAsync();
        using var folder = new TemporaryDirectory("legacy-library-browse");
        var opened = await context.Services.ProjectOpenService.OpenAsync(folder.Path);
        await context.Services.ProjectLibraryRepository.SubmitAsync(new LibrarySubmission(
            Guid.NewGuid(),
            opened.Project.Id,
            Guid.NewGuid(),
            null,
            "Legacy",
            "Continuity",
            "Imported legacy summary.",
            "legacy://library",
            context.Time.GetUtcNow()));

        var pane = new LibraryPaneViewModel(
            opened,
            () => Task.CompletedTask,
            evolutionLibrary: context.Services.ProjectLibraryEvolutionRepository);
        await pane.InitializeAsync();
        await pane.ShowCategoryAsync();

        Assert.Contains(pane.LibraryObjects, value => value.Topic == "Continuity");
        await pane.SelectLibraryObjectAsync(pane.LibraryObjects.Single(value => value.Topic == "Continuity").Id);
        Assert.All(pane.ObjectTimeline, value => Assert.Equal(LibraryTimelineContextKind.LegacyContext, value.ContextKind));

        Assert.Equal(0L, await CountAsync(context, "b1_project_governance"));
        Assert.Equal(0L, await CountAsync(context, "b1_authority_decisions"));
    }

    [Fact]
    public void Library_markup_exposes_both_axes_and_material_metadata()
    {
        var root = FindRepositoryRoot();
        var markup = File.ReadAllText(Path.Combine(root, "src", "Workbench.App", "Views", "Panes", "LibraryPaneView.axaml"));

        Assert.Contains("[Library.Category]", markup, StringComparison.Ordinal);
        Assert.Contains("[Library.Time]", markup, StringComparison.Ordinal);
        Assert.Contains("[Library.ObjectTimeline]", markup, StringComparison.Ordinal);
        Assert.Contains("MaterialKind", markup, StringComparison.Ordinal);
        Assert.Contains("Reference", markup, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AI.Game.Workbench.sln")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    private static async Task<long> CountAsync(AppTestContext context, string table)
    {
        await using var connection = context.Services.Database.CreateConnection();
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
